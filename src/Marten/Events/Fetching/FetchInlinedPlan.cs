using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events.Fetching;

internal class FetchInlinedPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class
{
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;

    internal FetchInlinedPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy)
    {
        _events = events;
        _identityStrategy = identityStrategy;
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate,
        CancellationToken cancellation = default)
    {
        IDocumentStorage<TDoc, TId> storage = null;
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            storage = session.Options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
            // Opt into the identity map mechanics for this aggregate type just in case
            // you're using a lightweight session
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = session.Options.ResolveCorrectedDocumentStorage<TDoc, TId>(session.TrackingMode);
        }

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, forUpdate);

        builder.StartNewCommand();

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
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
            var document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

            // As an optimization, put the document in the identity map for later
            if (document != null && session.Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, document);
            }

            return version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);
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
        long expectedStartingVersion, CancellationToken cancellation = default)
    {
        IDocumentStorage<TDoc, TId> storage = null;
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            storage = session.Options.ResolveCorrectedDocumentStorage<TDoc, TId>(DocumentTracking.IdentityOnly);
            // Opt into the identity map mechanics for this aggregate type just in case
            // you're using a lightweight session
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = session.StorageFor<TDoc, TId>();
        }

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var builder = new BatchBuilder { TenantId = session.TenantId };
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, false);
        builder.Append(";");

        builder.StartNewCommand();

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
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
            var document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

            // As an optimization, put the document in the identity map for later
            if (document != null && session.Options.Events.UseIdentityMapForAggregates)
            {
                session.StoreDocumentInItemMap(id, document);
            }

            return version == 0
                ? _identityStrategy.StartStream(document, session, id, cancellation)
                : _identityStrategy.AppendToStream(document, session, id, version, cancellation);
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
        IDocumentStorage<TDoc, TId> storage = null;
        if (session.Options.Events.UseIdentityMapForAggregates)
        {
            storage = (IDocumentStorage<TDoc, TId>)session.Options.Providers.StorageFor<TDoc>().IdentityMap;
            // Opt into the identity map mechanics for this aggregate type just in case
            // you're using a lightweight session
            session.UseIdentityMapFor<TDoc>();
        }
        else
        {
            storage = session.StorageFor<TDoc, TId>();
        }

        // Opting into optimizations here
        if (session.TryGetAggregateFromIdentityMap<TDoc, TId>(id, out var doc))
        {
            return doc;
        }

        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var builder = new BatchBuilder { TenantId = session.TenantId };
        builder.Append(";");

        var handler = new LoadByIdHandler<TDoc, TId>(storage, id);
        handler.ConfigureCommand(builder, session);

        await using var reader =
            await session.ExecuteReaderAsync(builder.Compile(), cancellation).ConfigureAwait(false);
        var document = await handler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

        return document;
    }
}
