using System;
using System.Linq;
using System.Linq.Expressions;
using Marten.Linq.Fields;
using Marten.Linq.Parsing;
using Marten.Schema;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events;

public class EventQueryMapping: DocumentMapping
{
    public EventQueryMapping(StoreOptions storeOptions): base(typeof(IEvent), storeOptions)
    {
        DatabaseSchemaName = storeOptions.Events.DatabaseSchemaName;

        TenancyStyle = storeOptions.Events.TenancyStyle;

        TableName = new PostgresqlObjectName(DatabaseSchemaName, "mt_events");

        duplicateField(x => x.Sequence, "seq_id");
        if (storeOptions.Events.StreamIdentity == StreamIdentity.AsGuid)
        {
            duplicateField(x => x.StreamId, "stream_id");
        }
        else
        {
            duplicateField(x => x.StreamKey, "stream_id");
        }

        duplicateField(x => x.Version, "version");
        duplicateField(x => x.Timestamp, "timestamp");
        duplicateField(x => x.IsArchived, "is_archived");

        duplicateField(x => x.EventTypeName, "type");
        duplicateField(x => x.DotNetTypeName, SchemaConstants.DotNetTypeColumn);


        if (storeOptions.EventGraph.Metadata.CorrelationId.Enabled)
        {
            duplicateField(x => x.CorrelationId, storeOptions.EventGraph.Metadata.CorrelationId.Name);
        }

        if (storeOptions.EventGraph.Metadata.CausationId.Enabled)
        {
            duplicateField(x => x.CausationId, storeOptions.EventGraph.Metadata.CausationId.Name);
        }
    }

    public override DbObjectName TableName { get; }

    private DuplicatedField duplicateField(Expression<Func<IEvent, object>> property, string columnName)
    {
        var finder = new FindMembers();
        finder.Visit(property);

        return DuplicateField(finder.Members.ToArray(), columnName: columnName);
    }
}
