using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventProgressionTable: Table
{
    public EventProgressionTable(string schemaName): base(new PostgresqlObjectName(schemaName, "mt_event_progression"))
    {
        AddColumn<string>("name").AsPrimaryKey();
        AddColumn("last_seq_id", "bigint").AllowNulls();
        AddColumn("last_updated", "timestamp with time zone")
            .DefaultValueByExpression("(transaction_timestamp())");

        PrimaryKeyName = "pk_mt_event_progression";
    }
}
