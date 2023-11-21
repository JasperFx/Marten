using System.IO;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;

namespace Marten.Events.Archiving;

internal class ArchiveStreamFunction: Function
{
    internal const string Name = "mt_archive_stream";

    private readonly EventGraph _events;

    public ArchiveStreamFunction(EventGraph events): base(new PostgresqlObjectName(events.DatabaseSchemaName, Name))
    {
        _events = events;
    }

    public override void WriteCreateStatement(Migrator rules, TextWriter writer)
    {
        var argList = _events.StreamIdentity == StreamIdentity.AsGuid
            ? "streamid uuid"
            : "streamid varchar";

        writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {_events.DatabaseSchemaName}.{Name}({argList}) RETURNS VOID LANGUAGE plpgsql AS
$function$
BEGIN
  update {_events.DatabaseSchemaName}.mt_streams set {IsArchivedColumn.ColumnName} = TRUE where id = streamid;
  update {_events.DatabaseSchemaName}.mt_events set is_archived = TRUE where stream_id = streamid;
END;
$function$;
");
    }
}
