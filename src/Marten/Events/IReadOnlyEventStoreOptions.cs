#nullable enable
using System.Collections.Generic;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events;

public interface IReadOnlyEventStoreOptions
{
    /// <summary>
    ///     Advanced configuration for the asynchronous projection execution
    /// </summary>
    IReadOnlyDaemonSettings Daemon { get; }

    /// <summary>
    ///     Configure whether event streams are identified with Guid or strings
    /// </summary>
    StreamIdentity StreamIdentity { get; }

    /// <summary>
    ///     Configure the event sourcing storage for multi-tenancy
    /// </summary>
    TenancyStyle TenancyStyle { get; }

    /// <summary>
    ///     Override the database schema name for event related tables. By default this
    ///     is the same schema as the document storage
    /// </summary>
    string DatabaseSchemaName { get; }

    /// <summary>
    ///     Metadata configuration
    /// </summary>
    IReadonlyMetadataConfig MetadataConfig { get; }

    /// <summary>
    /// Opt into having Marten create a unique index on Event.Id. The default is false. This may
    /// be helpful if you need to create an external reference id to another system, or need to
    /// load events by their Id
    /// </summary>
    bool EnableUniqueIndexOnEventId { get; set; }

    EventAppendMode AppendMode { get; set; }

    /// <summary>
    ///     Configuration for all event store projections
    /// </summary>
    IReadOnlyList<IReadOnlyProjectionData> Projections();

    IReadOnlyList<IEventType> AllKnownEventTypes();

    /// <summary>
    /// Opt into a performance optimization that directs Marten to always use the identity map for an
    /// Inline single stream projection's aggregate type when FetchForWriting() is called. Default is false.
    /// Do not use this if you manually alter the fetched aggregate from FetchForWriting() outside of Marten
    /// </summary>
    bool UseIdentityMapForInlineAggregates { get; set; }

    /// <summary>
    /// Opt into using PostgreSQL list partitioning. This can have significant performance and scalability benefits
    /// *if* you are also aggressively using event stream archiving
    /// </summary>
    bool UseArchivedStreamPartitioning { get; set; }

    /// <summary>
    /// Optional extension point to receive published messages as a side effect from
    /// aggregation projections
    /// </summary>
    IMessageOutbox MessageOutbox { get; set; }
}
