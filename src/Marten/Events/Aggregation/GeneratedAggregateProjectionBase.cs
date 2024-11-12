using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using JasperFx.Events;
using JasperFx.Events.CodeGeneration;
using JasperFx.Events.Projections;
using JasperFx.Events.Projections.Aggregation;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Storage;
using CreateMethodCollection = JasperFx.Events.CodeGeneration.CreateMethodCollection;

namespace Marten.Events.Aggregation;

public abstract partial class GeneratedAggregateProjectionBase<T>: GeneratedProjection, IAggregateProjection,
    ILiveAggregatorSource<T>, IAggregateProjectionWithSideEffects<T>
{
    private readonly Lazy<Type[]> _allEventTypes;
    internal readonly ApplyMethodCollection _applyMethods;

    internal readonly JasperFx.Events.CodeGeneration.CreateMethodCollection _createMethods;
    private readonly string _inlineAggregationHandlerType;
    private readonly string _liveAggregationTypeName;
    internal readonly ShouldDeleteMethodCollection _shouldDeleteMethods;
    private readonly AggregateVersioning<T> _versioning;
    internal IDocumentMapping _aggregateMapping;
    private GeneratedType _inlineGeneratedType;
    private Type _inlineType;
    private bool _isAsync;
    private GeneratedType _liveGeneratedType;
    private Type _liveType;
    private IAggregationRuntime _runtime;

    protected GeneratedAggregateProjectionBase(AggregationScope scope): base(typeof(T).NameInCode())
    {
        _createMethods = new CreateMethodCollection(GetType(), typeof(T), typeof(IQuerySession), typeof(IDocumentSession));
        _applyMethods = new ApplyMethodCollection(GetType(), typeof(T), typeof(IQuerySession));
        _shouldDeleteMethods = new ShouldDeleteMethodCollection(GetType(), typeof(T), typeof(IQuerySession));

        Options.DeleteViewTypeOnTeardown<T>();

        _allEventTypes = new Lazy<Type[]>(() =>
        {
            return _createMethods.Methods.Concat(_applyMethods.Methods).Concat(_shouldDeleteMethods.Methods)
                .Select(x => x.EventType).Concat(DeleteEvents).Concat(TransformedEvents).Distinct().ToArray();
        });


        _inlineAggregationHandlerType = GetType().ToSuffixedTypeName("InlineHandler");
        _liveAggregationTypeName = GetType().ToSuffixedTypeName("LiveAggregation");
        _versioning = new AggregateVersioning<T>(scope);

        RegisterPublishedType(typeof(T));

        if (typeof(T).TryGetAttribute<ProjectionVersionAttribute>(out var att))
        {
            ProjectionVersion = att.Version;
        }
    }

    internal IList<Type> DeleteEvents { get; } = new List<Type>();
    internal IList<Type> TransformedEvents { get; } = new List<Type>();

    // TODO -- duplicated with AggregationRuntime, and that's an ick.
    /// <summary>
    ///     If more than 0 (the default), this is the maximum number of aggregates
    ///     that will be cached in a 2nd level, most recently used cache during async
    ///     projection. Use this to potentially improve async projection throughput
    /// </summary>
    public int CacheLimitPerTenant { get; set; } = 0;

    Type IAggregateProjection.AggregateType => typeof(T);

    object IMetadataApplication.ApplyMetadata(object aggregate, IEvent lastEvent)
    {
        if (aggregate is T t)
        {
            return ApplyMetadata(t, lastEvent);
        }

        return aggregate;
    }

    public bool AppliesTo(IEnumerable<Type> eventTypes)
    {
        return eventTypes
            .Intersect(AllEventTypes).Any() || eventTypes.Any(type => AllEventTypes.Any(type.CanBeCastTo));
    }

    public Type[] AllEventTypes => _allEventTypes.Value;

    public bool MatchesAnyDeleteType(IEventSlice slice)
    {
        return slice.Events().Select(x => x.EventType).Intersect(DeleteEvents).Any();
    }

    public virtual void ConfigureAggregateMapping(IStorageMapping mapping, IEventGraph eventGraph)
    {
        // nothing
    }

    /// <summary>
    ///     Use to create "side effects" when running an aggregation (single stream, custom projection, multi-stream)
    ///     asynchronously in a continuous mode (i.e., not in rebuilds)
    /// </summary>
    /// <param name="operations"></param>
    /// <param name="slice"></param>
    /// <returns></returns>
    public virtual ValueTask RaiseSideEffects(IDocumentOperations operations, IEventSlice<T> slice)
    {
        return new ValueTask();
    }

    public abstract bool IsSingleStream();

    /// <summary>
    ///     Template method that is called on the last event in a slice of events that
    ///     are updating an aggregate. This was added specifically to add metadata like "LastModifiedBy"
    ///     from the last event to an aggregate with user-defined logic. Override this for your own specific logic
    /// </summary>
    /// <param name="aggregate"></param>
    /// <param name="lastEvent"></param>
    public virtual T ApplyMetadata(T aggregate, IEvent lastEvent)
    {
        return aggregate;
    }

    /// <summary>
    ///     Designate or override the aggregate version member for this aggregate type
    /// </summary>
    /// <param name="expression"></param>
    public void VersionIdentity(Expression<Func<T, int>> expression)
    {
        _versioning.Override(expression);
    }

    /// <summary>
    ///     Designate or override the aggregate version member for this aggregate type
    /// </summary>
    /// <param name="expression"></param>
    public void VersionIdentity(Expression<Func<T, long>> expression)
    {
        _versioning.Override(expression);
    }

    protected abstract object buildEventSlicer(StoreOptions options);
    protected abstract Type baseTypeForAggregationRuntime();

    /// <summary>
    ///     When used as an asynchronous projection, this opts into
    ///     only taking in events from streams explicitly marked as being
    ///     the aggregate type for this projection. Only use this if you are explicitly
    ///     marking streams with the aggregate type on StartStream()
    /// </summary>
    [MartenIgnore]
    public void FilterIncomingEventsOnStreamType()
    {
        StreamType = typeof(T);
    }

    protected sealed override ValueTask<EventRangeGroup> groupEvents(DocumentStore store,
        IMartenDatabase daemonDatabase, EventRange range,
        CancellationToken cancellationToken)
    {
        _runtime ??= BuildRuntime(store);

        return _runtime.GroupEvents(store, daemonDatabase, range, cancellationToken);
    }

    protected virtual Type[] determineEventTypes()
    {
        var eventTypes = MethodCollection.AllEventTypes(_applyMethods, _createMethods, _shouldDeleteMethods)
            .Concat(DeleteEvents).Concat(TransformedEvents).Distinct().ToArray();
        return eventTypes;
    }
}
