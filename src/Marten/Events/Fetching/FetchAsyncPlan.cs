using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.QueryHandlers;
using Marten.Schema;
using Marten.Storage;
using Npgsql;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events.Fetching;

internal class AsyncFetchPlanner: IFetchPlanner
{
    public bool TryMatch<TDoc, TId>(IDocumentStorage<TDoc, TId> storage, IEventIdentityStrategy<TId> identity, StoreOptions options,
        out IAggregateFetchPlan<TDoc, TId> plan) where TDoc : class
    {
        if (options.Projections.TryFindAggregate(typeof(TDoc), out var projection))
        {
            if (projection is MultiStreamProjection<TDoc, TId>)
            {
                throw new InvalidOperationException(
                    $"The aggregate type {typeof(TDoc).FullNameInCode()} is the subject of a multi-stream projection and cannot be used with FetchForWriting");
            }

            if (projection is CustomProjection<TDoc, TId> custom)
            {
                if (!(custom.Slicer is ISingleStreamSlicer))
                {
                    throw new InvalidOperationException(
                        $"The aggregate type {typeof(TDoc).FullNameInCode()} is the subject of a multi-stream projection and cannot be used with FetchForWriting");
                }
            }

            if (projection.Lifecycle == ProjectionLifecycle.Async)
            {
                var mapping = options.Storage.FindMapping(typeof(TDoc)) as DocumentMapping;
                if (mapping != null && mapping.Metadata.Revision.Enabled)
                {
                    plan = new FetchAsyncPlan<TDoc, TId>(options.EventGraph, identity, storage);
                    return true;
                }
            }
        }

        plan = default;
        return false;
    }
}

internal class FetchAsyncPlan<TDoc, TId>: IAggregateFetchPlan<TDoc, TId> where TDoc : class
{
    private readonly EventGraph _events;
    private readonly IEventIdentityStrategy<TId> _identityStrategy;
    private readonly IDocumentStorage<TDoc, TId> _storage;
    private readonly ILiveAggregator<TDoc> _aggregator;
    private readonly string _versionSelectionSql;
    private string _initialSql;

    public FetchAsyncPlan(EventGraph events, IEventIdentityStrategy<TId> identityStrategy,
        IDocumentStorage<TDoc, TId> storage)
    {
        _events = events;
        _identityStrategy = identityStrategy;
        _storage = storage;
        _aggregator = _events.Options.Projections.AggregatorFor<TDoc>();

        if (_events.TenancyStyle == TenancyStyle.Single)
        {
            _versionSelectionSql =
                $" left outer join {storage.TableName.QualifiedName} as a on d.stream_id = a.id where (a.mt_version is NULL or d.version > a.mt_version) and d.stream_id = ";
        }
        else
        {
            _versionSelectionSql =
                $" left outer join {storage.TableName.QualifiedName} as a on d.stream_id = a.id and d.tenant_id = a.tenant_id where (a.mt_version is NULL or d.version > a.mt_version) and d.stream_id = ";
        }


    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, bool forUpdate, CancellationToken cancellation = default)
    {
        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        _initialSql ??=
            $"select {selector.SelectFields().Select(x => "d." + x).Join(", ")} from {_events.DatabaseSchemaName}.mt_events as d";

        if (forUpdate)
        {
            await session.BeginTransactionAsync(cancellation).ConfigureAwait(false);
        }

        var builder = new BatchBuilder{TenantId = session.TenantId};
        if (!forUpdate)
        {
            builder.Append("begin transaction isolation level repeatable read read only");
            builder.StartNewCommand();
        }
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, forUpdate);

        builder.StartNewCommand();

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        if (!forUpdate)
        {
            builder.StartNewCommand();
            builder.Append("end");
        }

        long version = 0;
        try
        {
            var batch = builder.Compile();
            await using var reader =
                await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

            // Read the latest version
            if (await reader.ReadAsync(cancellation).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, cancellation).ConfigureAwait(false);
            }

            // Fetch the existing aggregate -- if any!
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

            // Read in any events from after the current state of the aggregate
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await new ListQueryHandler<IEvent>(null, selector).HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            if (events.Any())
            {
                document = await _aggregator.BuildAsync(events, session, document, cancellation).ConfigureAwait(false);
            }

            if (document != null)
            {
                _storage.SetIdentity(document, id);
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

    private void writeEventFetchStatement(TId id,
        BatchBuilder builder)
    {
        builder.Append(_initialSql);
        builder.Append(_versionSelectionSql);
        builder.AppendParameter(id);

        // You must do this for performance even if the stream ids were
        // magically unique across tenants
        if (_events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and d.tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }
    }

    public async Task<IEventStream<TDoc>> FetchForWriting(DocumentSessionBase session, TId id, long expectedStartingVersion,
        CancellationToken cancellation = default)
    {
        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        _initialSql ??=
            $"select {selector.SelectFields().Select(x => "d." + x).Join(", ")} from {_events.DatabaseSchemaName}.mt_events as d";

        // TODO -- use read only transaction????

        var builder = new BatchBuilder{TenantId = session.TenantId};
        _identityStrategy.BuildCommandForReadingVersionForStream(builder, id, false);

        builder.StartNewCommand();

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        long version = 0;
        try
        {
            var batch = builder.Compile();
            await using var reader =
                await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

            // Read the latest version
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

            // Fetch the existing aggregate -- if any!
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

            // Read in any events from after the current state of the aggregate
            await reader.NextResultAsync(cancellation).ConfigureAwait(false);
            var events = await new ListQueryHandler<IEvent>(null, selector).HandleAsync(reader, session, cancellation).ConfigureAwait(false);
            if (events.Any())
            {
                document = await _aggregator.BuildAsync(events, session, document, cancellation).ConfigureAwait(false);
            }

            if (document != null)
            {
                _storage.SetIdentity(document, id);
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

        await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation).ConfigureAwait(false);
        await session.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var selector = await _identityStrategy.EnsureEventStorageExists<TDoc>(session, cancellation)
            .ConfigureAwait(false);

        _initialSql ??=
            $"select {selector.SelectFields().Select(x => "d." + x).Join(", ")} from {_events.DatabaseSchemaName}.mt_events as d";

        // TODO -- use read only transaction????

        var builder = new BatchBuilder{TenantId = session.TenantId};

        var loadHandler = new LoadByIdHandler<TDoc, TId>(_storage, id);
        loadHandler.ConfigureCommand(builder, session);

        builder.StartNewCommand();

        writeEventFetchStatement(id, builder);

        var batch = builder.Compile();
        await using var reader =
            await session.ExecuteReaderAsync(batch, cancellation).ConfigureAwait(false);

        // Fetch the existing aggregate -- if any!
        var document = await loadHandler.HandleAsync(reader, session, cancellation).ConfigureAwait(false);

        // Read in any events from after the current state of the aggregate
        await reader.NextResultAsync(cancellation).ConfigureAwait(false);
        var events = await new ListQueryHandler<IEvent>(null, selector).HandleAsync(reader, session, cancellation).ConfigureAwait(false);
        if (events.Any())
        {
            document = await _aggregator.BuildAsync(events, session, document, cancellation).ConfigureAwait(false);
        }

        if (document != null)
        {
            _storage.SetIdentity(document, id);
        }

        return document;
    }
}

internal class AggregateEventFloor<TId>: ISqlFragment
{
    private readonly DbObjectName _tableName;
    private readonly TId _id;

    public AggregateEventFloor(DbObjectName tableName, TId id)
    {
        _tableName = tableName;
        _id = id;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("version > (select mt_version from ");
        builder.Append(_tableName.QualifiedName);
        builder.Append(" as a where a.id = ");
        builder.AppendParameter(_id);
        builder.Append(")");
    }
}
