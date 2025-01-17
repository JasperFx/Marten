using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal class FetchLivePlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class
{
    private readonly ILiveAggregator<TDoc> _aggregator;
    private readonly IDocumentStorage<TDoc, TId> _documentStorage;
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;

    public FetchLivePlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IDocumentStorage<TDoc, TId> documentStorage)
    {
        _events = events;
        _identityStrategy = identityStrategy;
        _documentStorage = documentStorage;
        _aggregator = _events.Options.Projections.AggregatorFor<TDoc>();
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default)
    {
        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, forUpdate);

        builder.StartNewCommand();

        var handler = _identityStrategy.BuildEventQueryHandler(id, selector);
        handler.ConfigureCommand(builder, session);

        long version = 0;
        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            var document = await _aggregator.BuildAsync(events, session, default, cancellation).ConfigureAwait(false);
            if (document != null)
            {
                _documentStorage.SetIdentity(document, id);
            }

            var stream = version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);

            // This is an optimization for calling FetchForWriting, then immediately calling FetchLatest
            if (session.Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, stream);
            }

            return stream;
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id,
        long expectedStartingVersion,
        CancellationToken cancellation = default)
    {

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, false);

        builder.StartNewCommand();

        var handler = _identityStrategy.BuildEventQueryHandler(id, selector);
        handler.ConfigureCommand(builder, session);

        long version = 0;
        try
        {
            await using var reader =
                await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            if (expectedStartingVersion != version)
            {
                throw new ConcurrencyException(
                    $"Expected the existing version to be {expectedStartingVersion}, but was {version}",
                    typeof(TDoc), id);
            }

            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            var document = await _aggregator.BuildAsync(events, session, default, cancellation).ConfigureAwait(false);

            var stream = version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);

            // This is an optimization for calling FetchForWriting, then immediately calling FetchLatest
            if (session.Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, stream);
            }

            return stream;
        }
        catch (Exception e)
        {
            if (e.InnerException is NpgsqlException { SqlState: PostgresErrorCodes.InFailedSqlTransaction })
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(id, e.InnerException);
            }

            throw;
        }
    }

    public async ValueTask<TDoc> FetchForReading(DocumentSessionBase session, TId id, CancellationToken cancellation)
    {
        // Optimization for having called FetchForWriting, then FetchLatest on same session in short order
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            if (session.TryGetAggregateFromIdentityMap<IEventStream<TDoc>, TId>(id, out var stream))
            {
                var starting = stream.Aggregate;
                var appendedEvents = stream.Events;

                return await _aggregator.BuildAsync(appendedEvents, session, starting, cancellation).ConfigureAwait(false);
            }
        }

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        var builder = new BatchBuilder{TenantId = session.TenantId};

        var handler = _identityStrategy.BuildEventQueryHandler(id, selector);
        handler.ConfigureCommand(builder, session);

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);

        var events = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);
        return await _aggregator.BuildAsync(events, session, default, cancellation).ConfigureAwait(false);
    }
}
