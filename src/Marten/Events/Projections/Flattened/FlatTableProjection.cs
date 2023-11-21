#nullable enable

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events.Daemon;
using Marten.Storage;
using Weasel.Core;
using Weasel.Postgresql.Tables;
using JasperFx.Core.Reflection;
using Weasel.Postgresql;
using FindMembers = Marten.Linq.Parsing.FindMembers;

namespace Marten.Events.Projections.Flattened;

/// <summary>
///     Projection type that will write event data to a single database table
/// </summary>
public partial class FlatTableProjection: GeneratedProjection, IProjectionSchemaSource
{
    private readonly List<IEventHandler> _handlers = new();
    private readonly string _inlineTypeName;

    public FlatTableProjection(string tableName, SchemaNameSource schemaNameSource): this(
        new PostgresqlObjectName("public", tableName), schemaNameSource)
    {
    }

    public FlatTableProjection(DbObjectName tableName): this(tableName, SchemaNameSource.Explicit) { }

    private FlatTableProjection(DbObjectName tableName, SchemaNameSource schemaNameSource): base(tableName.Name)
    {
        SchemaNameSource = schemaNameSource;
        Table = new Table(tableName);
        _inlineTypeName = GetType().ToSuffixedTypeName("InlineProjection");

        _generatedProjection = new Lazy<IProjection>(() =>
        {
            if (_generatedType == null)
            {
                throw new InvalidOperationException("The EventProjection has not created its inner IProjection");
            }

            var projection = (IProjection)Activator.CreateInstance(_generatedType)!;

            return projection!;
        });
    }

    public SchemaNameSource SchemaNameSource { get; }

    /// <summary>
    ///     The definition of the table being written to. You can use this to
    ///     modify the table structure or even add indexes
    /// </summary>
    public Table Table { get; }

    IEnumerable<ISchemaObject> IProjectionSchemaSource.CreateSchemaObjects(EventGraph events)
    {
        readSchema(events);

        yield return Table;

        foreach (var handler in _handlers)
        {
            foreach (var schemaObject in handler.BuildObjects(events, Table)) yield return schemaObject;
        }
    }

    /// <summary>
    ///     Write values for the designated event type to the table columns
    /// </summary>
    /// <param name="configure"></param>
    /// <param name="tablePrimaryKeySource">
    ///     If specified, tells the projection how to find the primary key value for the table
    ///     from the event data. If missing, the projection uses the event's stream identity by default
    /// </param>
    /// <typeparam name="T"></typeparam>
    public void Project<T>(Action<StatementMap<T>> configure, Expression<Func<T, object>>? tablePrimaryKeySource = null)
    {
        IncludeType<T>();

        assertReceivedEventType<T>();

        var members = tablePrimaryKeySource == null
            ? Array.Empty<MemberInfo>()
            : FindMembers.Determine(tablePrimaryKeySource);

        var map = new StatementMap<T>(this, members);

        configure(map);

        _handlers.Add(map);
    }

    private static void assertReceivedEventType<T>()
    {
        if (typeof(T).Closes(typeof(IEvent<>)))
        {
            throw new ArgumentOutOfRangeException("T",
                "IEvent<T> cannot be used as the event type in this usage. Please use the actual event type");
        }

        if (!typeof(T).IsConcrete())
        {
            throw new ArgumentOutOfRangeException("T",
                "The event type in this usage must be a specific concrete event type");
        }
    }

    /// <summary>
    ///     Direct the projection to delete a row when this event type is encountered
    /// </summary>
    /// <param name="tablePrimaryKeySource">
    ///     If specified, tells the projection how to find the primary key value for the table
    ///     from the event data. If missing, the projection uses the event's stream identity by default
    /// </param>
    /// <typeparam name="T"></typeparam>
    public void Delete<T>(Expression<Func<T, object>>? tablePrimaryKeySource = null)
    {
        IncludeType<T>();
        assertReceivedEventType<T>();

        var members = tablePrimaryKeySource == null
            ? Array.Empty<MemberInfo>()
            : FindMembers.Determine(tablePrimaryKeySource);

        _handlers.Add(new EventDeleter(typeof(T), members));
    }

    protected override ValueTask<EventRangeGroup> groupEvents(DocumentStore store, IMartenDatabase daemonDatabase,
        EventRange range,
        CancellationToken cancellationToken)
    {
        return new ValueTask<EventRangeGroup>(
            new TenantedEventRangeGroup(
                store,
                daemonDatabase,
                _generatedProjection.Value,
                Options,
                range,
                cancellationToken
            )
        );
    }

    private void readSchema(EventGraph events)
    {
        switch (SchemaNameSource)
        {
            case SchemaNameSource.DocumentSchema:
                Table.MoveToSchema(events.Options.DatabaseSchemaName);
                break;

            case SchemaNameSource.EventSchema:
                Table.MoveToSchema(events.DatabaseSchemaName);
                break;
        }

        Options.DeleteDataInTableOnTeardown(Table.Identifier);
    }
}
