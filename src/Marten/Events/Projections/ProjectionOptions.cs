using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Fetching;
using Marten.Exceptions;
using Marten.Subscriptions;

namespace Marten.Events.Projections;

public class ErrorHandlingOptions
{
    /// <summary>
    /// Should the daemon skip any "poison pill" events that fail in user projection code?
    /// </summary>
    public bool SkipApplyErrors { get; set; }

    /// <summary>
    /// Should the daemon skip any unknown event types encountered when trying to
    /// fetch events?
    /// </summary>
    public bool SkipUnknownEvents { get; set; }

    /// <summary>
    /// Should the daemon skip any events that experience serialization errors?
    /// </summary>
    public bool SkipSerializationErrors { get; set; }
}

/// <summary>
///     Used to register projections with Marten
/// </summary>
public class ProjectionOptions: DaemonSettings
{
    private readonly Dictionary<Type, object> _liveAggregateSources = new();
    private readonly StoreOptions _options;

    private Lazy<Dictionary<string, AsyncProjectionShard>> _asyncShards;
    private ImHashMap<Type, object> _liveAggregators = ImHashMap<Type, object>.Empty;

    internal readonly IFetchPlanner[] _builtInPlanners = [new InlineFetchPlanner(), new AsyncFetchPlanner(), new LiveFetchPlanner()];

    private readonly List<ISubscriptionSource> _subscriptions = new();

    internal ProjectionOptions(StoreOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Async daemon error handling policies while running in a rebuild mode. The defaults
    /// are to *not* skip any errors
    /// </summary>
    public ErrorHandlingOptions RebuildErrors { get; } = new();

    /// <summary>
    /// Async daemon error handling polices while running continuously. The defaults
    /// are to skip serialization errors, unknown events, and apply errors
    /// </summary>
    public ErrorHandlingOptions Errors { get; } = new()
    {
        SkipApplyErrors = true,
        SkipSerializationErrors = true,
        SkipUnknownEvents = true
    };

    internal IEnumerable<IFetchPlanner> allPlanners()
    {
        foreach (var planner in FetchPlanners)
        {
            yield return planner;
        }

        foreach (var planner in _builtInPlanners)
        {
            yield return planner;
        }
    }

    /// <summary>
    /// Any custom or extended IFetchPlanner strategies for customizing FetchForWriting() behavior
    /// </summary>
    public List<IFetchPlanner> FetchPlanners { get; } = new();

    internal IList<IProjectionSource> All { get; } = new List<IProjectionSource>();

    /// <summary>
    /// Opt into a performance optimization that directs Marten to always use the identity map for an
    /// Inline single stream projection's aggregate type when FetchForWriting() is called. Default is false.
    /// Do not use this if you manually alter the fetched aggregate from FetchForWriting() outside of Marten
    /// </summary>
    [Obsolete("Prefer UseIdentityMapForAggregates")]
    public bool UseIdentityMapForInlineAggregates
    {
        get => _options.Events.UseIdentityMapForAggregates;
        set => _options.Events.UseIdentityMapForAggregates = value;
    }

    /// <summary>
    /// Opt into a performance optimization that directs Marten to always use the identity map for an
    /// Inline single stream projection's aggregate type when FetchForWriting() is called. Default is false.
    /// Do not use this if you manually alter the fetched aggregate from FetchForWriting() outside of Marten
    /// </summary>
    public bool UseIdentityMapForAggregates
    {
        get => _options.Events.UseIdentityMapForAggregates;
        set => _options.Events.UseIdentityMapForAggregates = value;
    }

    internal bool HasAnyAsyncProjections()
    {
        return All.Any(x => x.Lifecycle == ProjectionLifecycle.Async) || _subscriptions.Any();
    }

    internal IEnumerable<Type> AllAggregateTypes()
    {
        foreach (var kv in _liveAggregators.Enumerate()) yield return kv.Key;

        foreach (var projection in All.OfType<IAggregateProjection>()) yield return projection.AggregateType;
    }

    public bool TryFindAggregate(Type documentType, out IAggregateProjection projection)
    {
        projection = All.OfType<IAggregateProjection>().FirstOrDefault(x => x.AggregateType == documentType);
        return projection != null;
    }

    internal IProjection[] BuildInlineProjections(DocumentStore store)
    {
        var inlineSources = All.Where(x => x.Lifecycle == ProjectionLifecycle.Inline).ToArray();

        return inlineSources.Select(x =>
        {
            try
            {
                return x.Build(store);
            }
            catch (Exception e)
            {
                Console.WriteLine("Error trying to build an IProjection for projection " + x.ProjectionName);
                Console.WriteLine(e.ToString());

                throw;
            }
        }).ToArray();
    }


    /// <summary>
    /// Register a projection to the Marten configuration
    /// </summary>
    /// <param name="projection">Value values are Inline/Async, The default is Inline</param>
    /// <param name="lifecycle"></param>
    /// <param name="projectionName">
    ///     Overwrite the named identity of this projection. This is valuable if using the projection
    ///     asynchronously
    /// </param>
    /// <param name="asyncConfiguration">
    ///     Optional configuration including teardown instructions for the usage of this
    ///     projection within the async projection daempon
    /// </param>
    public void Add(
        IProjection projection,
        ProjectionLifecycle lifecycle,
        string projectionName = null,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        if (lifecycle == ProjectionLifecycle.Live)
        {
            if (!projection.GetType().Closes(typeof(ILiveAggregator<>)))
            {
                throw new ArgumentOutOfRangeException(nameof(lifecycle),
                    $"{nameof(ProjectionLifecycle.Live)} cannot be used for IProjection");
            }

            if (projection is IAggregateProjection aggregateProjection)
            {
                _options.Storage.MappingFor(aggregateProjection.AggregateType).SkipSchemaGeneration = true;
            }
        }

        if (projection is ProjectionBase p)
        {
            p.AssembleAndAssertValidity();
            p.Lifecycle = lifecycle;
        }

        if (projection is IProjectionSource source)
        {
            asyncConfiguration?.Invoke(source.Options);
            All.Add(source);
        }
        else
        {
            var wrapper = new ProjectionWrapper(projection, lifecycle);
            if (projectionName.IsNotEmpty())
            {
                wrapper.ProjectionName = projectionName;
            }

            asyncConfiguration?.Invoke(wrapper.Options);
            All.Add(wrapper);
        }
    }

    /// <summary>
    /// Register a projection to the Marten configuration
    /// </summary>
    /// <param name="projection">Value values are Inline/Async, The default is Inline</param>
    /// <param name="lifecycle"></param>
    /// <param name="projectionName">
    ///     Overwrite the named identity of this projection. This is valuable if using the projection
    ///     asynchronously
    /// </param>
    /// <param name="asyncConfiguration">
    ///     Optional configuration including teardown instructions for the usage of this
    ///     projection within the async projection daempon
    /// </param>
    public void Register(
        IProjectionSource source,
        ProjectionLifecycle lifecycle,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        if (source is ProjectionBase p)
        {
            p.AssembleAndAssertValidity();
            p.Lifecycle = lifecycle;
        }

        if (lifecycle == ProjectionLifecycle.Live && source is IAggregateProjection aggregateProjection)
        {
            // Hack to address https://github.com/JasperFx/marten/issues/2610
            _options.Storage.MappingFor(aggregateProjection.AggregateType).SkipSchemaGeneration = true;
        }

        asyncConfiguration?.Invoke(source.Options);
        All.Add(source);
    }

    /// <summary>
    /// Add a projection that will be executed inline
    /// </summary>
    /// <param name="projection"></param>
    /// <param name="lifecycle">Optionally override the lifecycle of this projection. The default is Inline</param>
    /// <param name="asyncConfiguration">Use it to define behaviour during projection rebuilds</param>
    public void Add(
        EventProjection projection,
        ProjectionLifecycle lifecycle,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        projection.Lifecycle = lifecycle;

        asyncConfiguration?.Invoke(projection.Options);

        projection.AssembleAndAssertValidity();
        All.Add(projection);
    }


    /// <summary>
    /// Register live stream aggregation. It's needed for pre-building generated types
    /// (Read more in https://martendb.io/configuration/prebuilding.html).
    /// You don't need to call this method if you registered Snapshot for this entity type.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="asyncConfiguration">Use it to define behaviour during projection rebuilds</param>
    /// <returns>The extended storage configuration for entity T</returns>
    public MartenRegistry.DocumentMappingExpression<T> LiveStreamAggregation<T>(
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        var expression = singleStreamProjection<T>(ProjectionLifecycle.Live, null, asyncConfiguration);

        // Hack to address https://github.com/JasperFx/marten/issues/2610
        _options.Storage.MappingFor(typeof(T)).SkipSchemaGeneration = true;

        return expression;
    }

    /// <summary>
    /// Perform automated snapshot on each event for selected entity type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="lifecycle">Override the snapshot lifecycle. The default is Inline</param>
    /// <param name="asyncConfiguration">
    ///     Optional configuration including teardown instructions for the usage of this
    ///     projection within the async projection daemon
    /// </param>
    /// <returns>The extended storage configuration for document T</returns>
    public MartenRegistry.DocumentMappingExpression<T> Snapshot<T>(
        SnapshotLifecycle lifecycle,
        Action<AsyncOptions> asyncConfiguration = null
    ) =>
        singleStreamProjection<T>(lifecycle.Map(), null, asyncConfiguration);

    /// <summary>
    /// Perform automated snapshot on each event for selected entity type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="lifecycle">Override the snapshot lifecycle. The default is Inline</param>
    /// <param name="configureProjection">Use it to further customize the projection.</param>
    /// <param name="asyncConfiguration">
    ///     Optional configuration including teardown instructions for the usage of this
    ///     projection within the async projection daemon
    /// </param>
    /// <returns>The extended storage configuration for document T</returns>
    public MartenRegistry.DocumentMappingExpression<T> Snapshot<T>(
        SnapshotLifecycle lifecycle,
        Action<SingleStreamProjection<T>> configureProjection,
        Action<AsyncOptions> asyncConfiguration = null
    ) =>
        singleStreamProjection<T>(lifecycle.Map(), configureProjection, asyncConfiguration);

    private MartenRegistry.DocumentMappingExpression<T> singleStreamProjection<T>(
        ProjectionLifecycle lifecycle,
        Action<SingleStreamProjection<T>> configureProjection = null,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        // Make sure there's a DocumentMapping for the aggregate
        var expression = _options.Schema.For<T>();

        var source = new SingleStreamProjection<T> { Lifecycle = lifecycle };

        configureProjection?.Invoke(source);

        asyncConfiguration?.Invoke(source.Options);

        source.AssembleAndAssertValidity();

        All.Add(source);

        return expression;
    }

    /// <summary>
    /// Register an aggregate projection that should be evaluated inline
    /// </summary>
    /// <typeparam name="TProjection">Projection type</typeparam>
    /// <param name="lifecycle">Optionally override the ProjectionLifecycle</param>
    /// <param name="asyncConfiguration">Use it to define behaviour during projection rebuilds</param>
    public void Add<TProjection>(
        ProjectionLifecycle lifecycle,
        Action<AsyncOptions> asyncConfiguration = null
    )
        where TProjection : GeneratedProjection, new()
    {
        if (lifecycle == ProjectionLifecycle.Live)
        {
            throw new InvalidOperationException("The generic overload of Add does not support Live projections, please use the non-generic overload.");
        }

        var projection = new TProjection { Lifecycle = lifecycle };

        asyncConfiguration?.Invoke(projection.Options);

        projection.AssembleAndAssertValidity();

        All.Add(projection);
    }

    /// <summary>
    /// Register an aggregate projection
    /// </summary>
    /// <param name="projection"></param>
    /// <typeparam name="T"></typeparam>
    /// <param name="lifecycle">Optionally override the ProjectionLifecycle</param>
    /// <param name="asyncConfiguration">Use it to define behaviour during projection rebuilds</param>
    public void Add<T>(
        GeneratedAggregateProjectionBase<T> projection,
        ProjectionLifecycle lifecycle,
        Action<AsyncOptions> asyncConfiguration = null
    )
    {
        projection.Lifecycle = lifecycle;

        asyncConfiguration?.Invoke(projection.Options);

        projection.AssembleAndAssertValidity();

        if (lifecycle == ProjectionLifecycle.Live)
        {
            // Hack to address https://github.com/JasperFx/marten/issues/3140
            _options.Storage.MappingFor(typeof(T)).SkipSchemaGeneration = true;
        }

        All.Add(projection);
    }

    /// <summary>
    /// Add a new event subscription to this store
    /// </summary>
    /// <param name="subscription"></param>
    public void Subscribe(ISubscriptionSource subscription)
    {
        _subscriptions.Add(subscription);
    }

    /// <summary>
    /// Add a new event subscription to this store with the option to configure the filtering
    /// and async daemon behavior
    /// </summary>
    /// <param name="subscription"></param>
    /// <param name="configure"></param>
    public void Subscribe(ISubscription subscription, Action<ISubscriptionOptions>? configure = null)
    {
        var source = subscription as ISubscriptionSource ?? new SubscriptionWrapper(subscription);

        if (source is ISubscriptionOptions options)
        {
            configure?.Invoke(options);
        }
        else if (configure != null)
        {
            throw new InvalidOperationException("Unable to apply subscription options to " + subscription);
        }

        _subscriptions.Add(source);
    }

    internal bool Any()
    {
        return All.Any() || _subscriptions.Any();
    }

    public ILiveAggregator<T> AggregatorFor<T>() where T : class
    {
        if (_liveAggregators.TryFind(typeof(T), out var aggregator))
        {
            return (ILiveAggregator<T>)aggregator;
        }

        aggregator = All.OfType<ILiveAggregator<T>>().FirstOrDefault();
        if (aggregator != null)
        {
            _liveAggregators = _liveAggregators.AddOrUpdate(typeof(T), aggregator);
            return (ILiveAggregator<T>)aggregator;
        }

        var source = tryFindProjectionSourceForAggregateType<T>();
        source.AssembleAndAssertValidity();

        aggregator = source.As<ILiveAggregatorSource<T>>().Build(_options);
        _liveAggregators = _liveAggregators.AddOrUpdate(typeof(T), aggregator);

        return (ILiveAggregator<T>)aggregator;
    }

    private SingleStreamProjection<T> tryFindProjectionSourceForAggregateType<T>() where T : class
    {
        var candidate = All.OfType<SingleStreamProjection<T>>().FirstOrDefault();
        if (candidate != null)
        {
            return candidate;
        }

        if (!_liveAggregateSources.TryGetValue(typeof(T), out var source))
        {
            return new SingleStreamProjection<T>();
        }

        return source as SingleStreamProjection<T>;
    }

    internal void AssertValidity(DocumentStore store)
    {
        var duplicateNames = All.Select(x => x.ProjectionName).Concat(_subscriptions.Select(x => x.SubscriptionName))
            .GroupBy(x => x)
            .Where(x => x.Count() > 1)
            .Select(group => $"Duplicate projection or subscription name '{group.Key}': {group.Select(x => x.ToString()).Join(", ")}")
            .ToArray();

        if (duplicateNames.Any())
        {
            throw new DuplicateSubscriptionNamesException(duplicateNames.Join("; "));
        }

        var messages = All.Concat(_liveAggregateSources.Values)
            .OfType<GeneratedProjection>()
            .Distinct()
            .SelectMany(x => x.ValidateConfiguration(_options))
            .ToArray();

        _asyncShards = new Lazy<Dictionary<string, AsyncProjectionShard>>(() =>
        {
            return All
                .Where(x => x.Lifecycle == ProjectionLifecycle.Async)
                .SelectMany(x => x.AsyncProjectionShards(store))
                .Concat(_subscriptions.SelectMany(x => x.AsyncProjectionShards(store)))
                .ToDictionary(x => x.Name.Identity);
        });

        if (messages.Any())
        {
            throw new InvalidProjectionException(messages);
        }
    }

    // This has to be public for CritterStackPro
    public IReadOnlyList<AsyncProjectionShard> AllShards()
    {
        return _asyncShards.Value.Values.ToList();
    }
    internal bool TryFindAsyncShard(string projectionOrShardName, out AsyncProjectionShard shard)
    {
        return _asyncShards.Value.TryGetValue(projectionOrShardName, out shard);
    }

    internal bool TryFindProjection(string projectionName, out IProjectionSource source)
    {
        source = All.FirstOrDefault(x => x.ProjectionName.EqualsIgnoreCase(projectionName));
        return source != null;
    }

    internal bool TryFindSubscription(string projectionName, out ISubscriptionSource source)
    {
        source = _subscriptions.FirstOrDefault(x => x.SubscriptionName.EqualsIgnoreCase(projectionName));
        return source != null;
    }


    internal string[] AllProjectionNames()
    {
        return All.Select(x => $"'{x.ProjectionName}'").Concat(_subscriptions.Select(x => $"'{x.SubscriptionName}'")).ToArray();
    }

    internal IEnumerable<Type> AllPublishedTypes()
    {
        return All.Where(x => x.Lifecycle != ProjectionLifecycle.Live).SelectMany(x => x.PublishedTypes()).Distinct();
    }

    internal ShardName[] AsyncShardsPublishingType(Type aggregationType)
    {
        var sources = All.Where(x => x.Lifecycle == ProjectionLifecycle.Async && x.PublishedTypes().Contains(aggregationType)).Select(x => x.ProjectionName).ToArray();
        return _asyncShards.Value.Values.Where(x => sources.Contains(x.Name.ProjectionName)).Select(x => x.Name).ToArray();
    }
}

public class DuplicateSubscriptionNamesException: MartenException
{
    public DuplicateSubscriptionNamesException(string message) : base(message)
    {
    }
}
