using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Services;
using Marten.Sessions;
using Marten.Storage;

namespace Marten.Events.Aggregation;

/// <summary>
/// Helpful as a base class for more custom aggregation projections that are not supported
/// by the Single/MultipleStreamProjections
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public abstract class CustomProjection<TDoc, TId>: ProjectionBase, IAggregationRuntime<TDoc, TId>, IProjectionSource
{
    private IDocumentStorage<TDoc, TId> _storage;

    protected CustomProjection()
    {
        ProjectionName = GetType().NameInCode();

        if (typeof(TId) == typeof(Guid))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamId<TDoc>();
        }
        else if (typeof(TId) == typeof(string))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamKey<TDoc>();
        }
    }

    public IEventSlicer<TDoc, TId> Slicer { get; protected internal set; }

    void IProjection.Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
#pragma warning disable VSTHRD002
        this.As<IProjection>().ApplyAsync(operations, streams, CancellationToken.None).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }

    async Task IProjection.ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
        CancellationToken cancellation)
    {
        var slices = await Slicer.SliceInlineActions(operations, streams).ConfigureAwait(false);

        var martenSession = (DocumentSessionBase)operations;

        await martenSession.Database.EnsureStorageExistsAsync(typeof(TDoc), cancellation).ConfigureAwait(false);

        var storage = (IDocumentStorage<TDoc, TId>)martenSession.StorageFor<TDoc>();
        foreach (var slice in slices)
        {
            var tenantedSession = martenSession.UseTenancyBasedOnSliceAndStorage(storage, slice);

            // do not load if sliced by stream and the stream does not yet exist
            if (Slicer is not ISingleStreamSlicer || slice.ActionType != StreamActionType.Start)
            {
                slice.Aggregate = await storage.LoadAsync(slice.Id, tenantedSession, cancellation).ConfigureAwait(false);
            }
            await ApplyChangesAsync(tenantedSession, slice, cancellation).ConfigureAwait(false);
        }
    }

    async ValueTask<EventRangeGroup> IAggregationRuntime.GroupEvents(DocumentStore store, IMartenDatabase database,
        EventRange range,
        CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession(SessionOptions.ForDatabase(database));
        var groups = await Slicer.SliceAsyncEvents(session, range.Events).ConfigureAwait(false);

        return new TenantSliceRange<TDoc, TId>(store, this, range, groups, cancellationToken);
    }

    /// <summary>
    ///     Apply any document changes based on the incoming slice of events to the underlying aggregate document
    /// </summary>
    /// <param name="session"></param>
    /// <param name="slice"></param>
    /// <param name="cancellation"></param>
    /// <param name="lifecycle"></param>
    /// <returns></returns>
    public abstract ValueTask ApplyChangesAsync(DocumentSessionBase session, EventSlice<TDoc, TId> slice,
        CancellationToken cancellation,
        ProjectionLifecycle lifecycle = ProjectionLifecycle.Inline);

    public IAggregateVersioning Versioning { get; set; }

    /// <summary>
    ///     Override to give Marten "hints" about whether the aggregate is all new based on the incoming
    ///     event slice. The default implementation is always false.
    /// </summary>
    /// <param name="slice"></param>
    /// <returns></returns>
    public virtual bool IsNew(EventSlice<TDoc, TId> slice)
    {
        return false;
    }

    IDocumentStorage<TDoc, TId> IAggregationRuntime<TDoc, TId>.Storage => _storage;


    /// <summary>
    ///     Must be overridden to use as an async projection. Takes a range of events, and sorts them
    ///     into an EventSlice for each detected aggregate document
    /// </summary>
    /// <param name="store"></param>
    /// <param name="database"></param>
    /// <param name="range"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    /// <exception cref="NotSupportedException"></exception>
    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> GroupEventRange(DocumentStore store,
        IMartenDatabase database,
        EventRange range, CancellationToken cancellation)
    {
        using var session = store.LightweightSession(SessionOptions.ForDatabase(database));
        return Slicer.SliceAsyncEvents(session, range.Events);
    }

    Type IReadOnlyProjectionData.ProjectionType => GetType();
    AsyncOptions IProjectionSource.Options { get; } = new();

    IEnumerable<Type> IProjectionSource.PublishedTypes()
    {
        yield return typeof(TDoc);
    }

    IReadOnlyList<AsyncProjectionShard> IProjectionSource.AsyncProjectionShards(DocumentStore store)
    {
        readDocumentStorage(store);

        return new List<AsyncProjectionShard> { new(this)
        {
            IncludeArchivedEvents = false,
            EventTypes = IncludedEventTypes,
            StreamType = StreamType
        } };
    }

    async ValueTask<EventRangeGroup> IProjectionSource.GroupEvents(DocumentStore store, IMartenDatabase daemonDatabase,
        EventRange range,
        CancellationToken cancellationToken)
    {
        var groups = await GroupEventRange(store, daemonDatabase, range, cancellationToken).ConfigureAwait(false);

        return new TenantSliceRange<TDoc, TId>(store, this, range, groups, cancellationToken);
    }

    IProjection IProjectionSource.Build(DocumentStore store)
    {
        readDocumentStorage(store);
        return this;
    }

    internal override void AssembleAndAssertValidity()
    {
        if (Slicer == null)
        {
            throw new InvalidProjectionException(
                $"Projection {GetType().FullNameInCode()} does not have a configured event slicer.");
        }

        if (Slicer is EventSlicer<TDoc, TId> slicer && !slicer.HasAnyRules())
        {
            throw new InvalidProjectionException(
                $"Projection {GetType().FullNameInCode()} has incomplete event slicer configuration.");
        }
    }

    public void UseCustomSlicer(IEventSlicer<TDoc, TId> custom)
    {
        Slicer = custom;
    }

    private void readDocumentStorage(DocumentStore store)
    {
        var storage = store.Options.Providers.StorageFor<TDoc>();
        _storage = storage.Lightweight as IDocumentStorage<TDoc, TId>;
        if (_storage == null)
        {
            throw new InvalidOperationException(
                $"Document type {typeof(TDoc).FullNameInCode()} has identity type {storage.QueryOnly.IdType.NameInCode()}, but projection {GetType().FullNameInCode()} is defined with id type {typeof(TId).NameInCode()}");
        }
    }

    /// <summary>
    ///     Configure event aggregation "slicing" using Marten's default, configurable event
    ///     slicer
    /// </summary>
    /// <param name="configure"></param>
    public void AggregateEvents(Action<EventSlicer<TDoc, TId>> configure)
    {
        var slicer = new EventSlicer<TDoc, TId>();
        configure(slicer);

        Slicer = slicer;
    }

    /// <summary>
    ///     Aggregate events by the containing stream identity
    /// </summary>
    public void AggregateByStream()
    {
        if (typeof(TId) == typeof(Guid))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamId<TDoc>();
        }
        else if (typeof(TId) == typeof(string))
        {
            Slicer = (IEventSlicer<TDoc, TId>)new ByStreamKey<TDoc>();
        }
        else
        {
            throw new InvalidProjectionException(
                $"Invalid identity type {typeof(TId).NameInCode()} for aggregating by stream in projection {GetType().FullNameInCode()}");
        }
    }
}

