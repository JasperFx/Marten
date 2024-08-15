using System.Collections.Generic;
using System.Linq;
using Marten.Events.Archiving;
using Marten.Storage;
using Marten.Storage.Metadata;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

#region sample_EventsTable

internal class EventsTable: Table
{
    public EventsTable(EventGraph events): base(new PostgresqlObjectName(events.DatabaseSchemaName, "mt_events"))
    {
        AddColumn(new SequenceColumn()).AsPrimaryKey();
        AddColumn(new EventTableColumn("id", x => x.Id)).NotNull();
        AddColumn(new StreamIdColumn(events));

        AddColumn(new VersionColumn());
        AddColumn<EventJsonDataColumn>();
        AddColumn<EventTypeColumn>();
        AddColumn(new EventTableColumn("timestamp", x => x.Timestamp)).NotNull().DefaultValueByString("(now())");

        AddColumn<TenantIdColumn>();

        AddColumn<DotNetTypeColumn>().AllowNulls();

        AddIfActive(events.Metadata.CorrelationId);
        AddIfActive(events.Metadata.CausationId);
        AddIfActive(events.Metadata.Headers);

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            if (events.UseArchivedStreamPartitioning)
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_tenant_id_is_archived")
                {
                    ColumnNames = new[] { TenantIdColumn.Name, "stream_id", "is_archived" },
                    LinkedNames = new[] { TenantIdColumn.Name, "id", "is_archived" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams")
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { TenantIdColumn.Name, "stream_id", "version", "is_archived" }
                });
            }
            else
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_tenant_id")
                {
                    ColumnNames = new[] { TenantIdColumn.Name, "stream_id" },
                    LinkedNames = new[] { TenantIdColumn.Name, "id" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams")
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { TenantIdColumn.Name, "stream_id", "version" }
                });
            }


        }
        else
        {
            if (events.UseArchivedStreamPartitioning)
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id_is_archived")
                {
                    ColumnNames = new[] { "stream_id", "is_archived" },
                    LinkedNames = new[] { "id", "is_archived" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams"),
                    OnDelete = CascadeAction.Cascade
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { "stream_id", "version", "is_archived" }
                });
            }
            else
            {
                ForeignKeys.Add(new ForeignKey("fkey_mt_events_stream_id")
                {
                    ColumnNames = new[] { "stream_id" },
                    LinkedNames = new[] { "id" },
                    LinkedTable = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_streams"),
                    OnDelete = CascadeAction.Cascade
                });

                Indexes.Add(new IndexDefinition("pk_mt_events_stream_and_version")
                {
                    IsUnique = true, Columns = new[] { "stream_id", "version" }
                });
            }
        }

        if (events.EnableUniqueIndexOnEventId)
        {
            Indexes.Add(new IndexDefinition("idx_mt_events_event_id")
            {
                IsUnique = true, Columns = ["id"]
            });
        }

        var archiving = AddColumn<IsArchivedColumn>();
        if (events.UseArchivedStreamPartitioning)
        {
            archiving.PartitionByListValues().AddPartition("archived", true);
        }
    }

    internal IList<IEventTableColumn> SelectColumns()
    {
        var columns = new List<IEventTableColumn>();
        columns.AddRange(Columns.OfType<IEventTableColumn>());

        var data = columns.OfType<EventJsonDataColumn>().Single();
        var typeName = columns.OfType<EventTypeColumn>().Single();
        var dotNetTypeName = columns.OfType<DotNetTypeColumn>().Single();
        var timestamp = columns.OfType<EventTableColumn>().Single(x => x.Name == "timestamp");

        columns.Remove(data);
        columns.Insert(0, data);
        columns.Remove(typeName);
        columns.Insert(1, typeName);
        columns.Remove(dotNetTypeName);
        columns.Insert(2, dotNetTypeName);
        columns.Remove(timestamp);
        columns.Insert(3, timestamp);

        return columns;
    }

    private void AddIfActive(MetadataColumn column)
    {
        if (column.Enabled)
        {
            AddColumn(column);
        }
    }
}

#endregion
