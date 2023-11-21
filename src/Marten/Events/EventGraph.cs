#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Events.Schema;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Services.Json.Transformations;
using Marten.Storage;
using Marten.Util;
using Microsoft.Extensions.Logging.Abstractions;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using static Marten.Events.EventMappingExtensions;

namespace Marten.Events;

public partial class EventGraph: IEventStoreOptions, IReadOnlyEventStoreOptions, IDisposable, IAsyncDisposable
{
    private readonly Cache<Type, string> _aggregateNameByType =
        new(type => type.Name.ToTableAlias());

    private readonly Cache<string, Type> _aggregateTypeByName;

    private readonly Cache<string, EventMapping?> _byEventName = new();

    private readonly Cache<Type, EventMapping> _events = new();

    private readonly Lazy<IProjection[]> _inlineProjections;

    private readonly Ref<ImHashMap<string, Type>> _nameToType = Ref.Of(ImHashMap<string, Type>.Empty);

    private string? _databaseSchemaName;

    private DocumentStore _store;
    private StreamIdentity _streamIdentity = StreamIdentity.AsGuid;
    private readonly CancellationTokenSource _cancellation = new();

    internal EventGraph(StoreOptions options)
    {
        StreamIdentity = StreamIdentity.AsGuid;
        Options = options;
        _events.OnMissing = eventType =>
        {
            var mapping = typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);
            Options.Storage.AddMapping(mapping);

            return mapping;
        };

        _byEventName.OnMissing = name => AllEvents().FirstOrDefault(x => x.EventTypeName == name);

        _inlineProjections = new Lazy<IProjection[]>(() => options.Projections.BuildInlineProjections(_store));

        _aggregateTypeByName = new Cache<string, Type>(findAggregateType);
    }

    internal NpgsqlDbType StreamIdDbType { get; private set; }

    internal StoreOptions Options { get; }

    internal DbObjectName Table => new PostgresqlObjectName(DatabaseSchemaName, "mt_events");

    internal EventMetadataCollection Metadata { get; } = new();

    /// <summary>
    ///     Configure whether event streams are identified with Guid or strings
    /// </summary>
    public StreamIdentity StreamIdentity
    {
        get => _streamIdentity;
        set
        {
            _streamIdentity = value;
            StreamIdDbType = value == StreamIdentity.AsGuid ? NpgsqlDbType.Uuid : NpgsqlDbType.Varchar;
        }
    }

    /// <summary>
    ///     Configure the event sourcing storage for multi-tenancy
    /// </summary>
    public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

    public bool EnableGlobalProjectionsForConjoinedTenancy { get; set; }

    /// <summary>
    ///     Configure the meta data required to be stored for events. By default meta data fields are disabled
    /// </summary>
    public MetadataConfig MetadataConfig => new(Metadata);

    /// <summary>
    ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
    ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
    ///     the event type. It can also be used for the event namespace migration.
    /// </summary>
    /// <typeparam name="TEvent"></typeparam>
    /// <returns>Event store options, to allow fluent definition</returns>
    public IEventStoreOptions AddEventType<TEvent>()
    {
        AddEventType(typeof(TEvent));
        return this;
    }

    /// <summary>
    ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
    ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
    ///     the event type
    /// </summary>
    /// <param name="eventType"></param>
    public void AddEventType(Type eventType)
    {
        _events.FillDefault(eventType);
    }

    /// <summary>
    ///     Register an event type with Marten. This isn't strictly necessary for normal usage,
    ///     but can help Marten with asynchronous projections where Marten hasn't yet encountered
    ///     the event type
    /// </summary>
    /// <param name="types"></param>
    public void AddEventTypes(IEnumerable<Type> types)
    {
        types.Each(AddEventType);
    }

    public void MapEventType<TEvent>(string eventTypeName) where TEvent : class
    {
        MapEventType(typeof(TEvent), eventTypeName);
    }

    public void MapEventType(Type eventType, string eventTypeName)
    {
        var eventMapping = EventMappingFor(eventType);
        eventMapping.EventTypeName = eventTypeName;
    }

    public IEventStoreOptions Upcast<TEvent>(
        string eventTypeName,
        JsonTransformation jsonTransformation = null
    ) where TEvent : class
    {
        return Upcast(typeof(TEvent), eventTypeName, jsonTransformation);
    }

    public IEventStoreOptions Upcast(
        Type eventType,
        string eventTypeName,
        JsonTransformation jsonTransformation = null
    )
    {
        var eventMapping = typeof(EventMapping<>).CloseAndBuildAs<EventMapping>(this, eventType);
        eventMapping.EventTypeName = eventTypeName;
        eventMapping.JsonTransformation(jsonTransformation);

        _byEventName.Fill(eventTypeName, eventMapping);

        return this;
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        string eventTypeName,
        Func<TOldEvent, TEvent> upcast
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), eventTypeName, JsonTransformations.Upcast(upcast));
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        Func<TOldEvent, TEvent> upcast
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), GetEventTypeName<TOldEvent>(), JsonTransformations.Upcast(upcast));
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        string eventTypeName,
        Func<TOldEvent, CancellationToken, Task<TEvent>> upcastAsync
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), eventTypeName, JsonTransformations.Upcast(upcastAsync));
    }

    public IEventStoreOptions Upcast<TOldEvent, TEvent>(
        Func<TOldEvent, CancellationToken, Task<TEvent>> upcastAsync
    ) where TOldEvent : class where TEvent : class
    {
        return Upcast(typeof(TEvent), GetEventTypeName<TOldEvent>(), JsonTransformations.Upcast(upcastAsync));
    }

    public IEventStoreOptions Upcast(params IEventUpcaster[] upcasters)
    {
        foreach (var upcaster in upcasters)
        {
            Upcast(
                upcaster.EventType,
                upcaster.EventTypeName,
                new JsonTransformation(upcaster.FromDbDataReader, upcaster.FromDbDataReaderAsync)
            );
        }

        return this;
    }

    public IEventStoreOptions Upcast<TUpcaster>() where TUpcaster : IEventUpcaster, new()
    {
        var upcaster = new TUpcaster();

        Upcast(
            upcaster.EventType,
            upcaster.EventTypeName,
            new JsonTransformation(upcaster.FromDbDataReader, upcaster.FromDbDataReaderAsync)
        );

        return this;
    }

    /// <summary>
    ///     Override the database schema name for event related tables. By default this
    ///     is the same schema as the document storage
    /// </summary>
    public string DatabaseSchemaName
    {
        get => _databaseSchemaName ?? Options.DatabaseSchemaName;
        set => _databaseSchemaName = value.ToLowerInvariant();
    }

    IReadOnlyDaemonSettings IReadOnlyEventStoreOptions.Daemon => _store.Options.Projections;

    IReadOnlyList<IReadOnlyProjectionData> IReadOnlyEventStoreOptions.Projections()
    {
        return Options.Projections.All.OfType<IReadOnlyProjectionData>().ToList();
    }

    public IReadOnlyList<IEventType> AllKnownEventTypes()
    {
        return _events.OfType<IEventType>().ToList();
    }

    IReadonlyMetadataConfig IReadOnlyEventStoreOptions.MetadataConfig => MetadataConfig;

    private Type findAggregateType(string name)
    {
        foreach (var aggregateType in Options.Projections.AllAggregateTypes())
        {
            var possibleName = _aggregateNameByType[aggregateType];
            if (name.EqualsIgnoreCase(possibleName))
            {
                return aggregateType;
            }
        }

        return null;
    }

    internal EventMapping EventMappingFor(Type eventType)
    {
        return _events[eventType];
    }

    internal EventMapping EventMappingFor<T>() where T : class
    {
        return EventMappingFor(typeof(T));
    }

    internal IEnumerable<EventMapping> AllEvents()
    {
        return _events;
    }

    internal EventMapping? EventMappingFor(string eventType)
    {
        return _byEventName[eventType];
    }

    // Fetch additional event aliases that map to these types
    internal IReadOnlySet<string> AliasesForEvents(IReadOnlyCollection<Type> types)
    {
        var aliases = new HashSet<string>();

        foreach (var mapping in _byEventName)
        {
            if (mapping is null)
                continue;
            if (types.Contains(mapping.DocumentType))
            {
                aliases.Add(mapping.Alias);
            }
        }

        return aliases;
    }

    internal bool IsActive(StoreOptions options)
    {
        return _events.Any() || Options.Projections.All.Any();
    }

    internal Type AggregateTypeFor(string aggregateTypeName)
    {
        return _aggregateTypeByName[aggregateTypeName];
    }

    internal string AggregateAliasFor(Type aggregateType)
    {
        var alias = _aggregateNameByType[aggregateType];

        _aggregateTypeByName.Fill(alias, aggregateType);

        return alias;
    }

    internal string GetStreamIdDBType()
    {
        return StreamIdentity == StreamIdentity.AsGuid ? "uuid" : "varchar";
    }

    internal Type GetStreamIdType()
    {
        return StreamIdentity == StreamIdentity.AsGuid ? typeof(Guid) : typeof(string);
    }

    internal Type TypeForDotNetName(string assemblyQualifiedName)
    {
        if (!_nameToType.Value.TryFind(assemblyQualifiedName, out var value))
        {
            value = Type.GetType(assemblyQualifiedName);
            if (value == null)
            {
                throw new UnknownEventTypeException($"Unable to load event type '{assemblyQualifiedName}'.");
            }

            _nameToType.Swap(n => n.AddOrUpdate(assemblyQualifiedName, value));
        }

        return value;
    }

    internal IEventStorage EnsureAsStringStorage(IMartenSession session)
    {
        if (StreamIdentity == StreamIdentity.AsGuid)
        {
            throw new InvalidOperationException(
                "This Marten event store is configured to identify streams with Guids");
        }

        return session.EventStorage();
    }

    internal IEventStorage EnsureAsGuidStorage(IMartenSession session)
    {
        if (StreamIdentity == StreamIdentity.AsString)
        {
            throw new InvalidOperationException(
                "This Marten event store is configured to identify streams with strings");
        }

        return session.EventStorage();
    }

    internal IEvent BuildEvent(object eventData)
    {
        if (eventData == null)
        {
            throw new ArgumentNullException(nameof(eventData));
        }

        var mapping = EventMappingFor(eventData.GetType());
        return mapping.Wrap(eventData);
    }

    internal void Initialize(DocumentStore store)
    {
        _store = store;

        var logger = (_store.Options.Logger() as DefaultMartenLogger)?.Inner ?? NullLogger.Instance;


        _tombstones = new RetryBlock<UpdateBatch>(executeTombstoneBlock, logger, _cancellation.Token);
        foreach (var mapping in _events)
        {
            mapping.JsonTransformation(null);
        }
    }

    private bool _isDisposed;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cancellation.Cancel();
        _cancellation.Dispose();
        _tombstones?.SafeDispose();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _tombstones.DrainAsync().ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            // Ignore this
        }

        Dispose();
    }
}
