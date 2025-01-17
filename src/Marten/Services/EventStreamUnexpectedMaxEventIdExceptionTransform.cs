using System;
using System.Text.RegularExpressions;
using JasperFx.Core;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Services;

internal class EventStreamUnexpectedMaxEventIdExceptionTransform: IExceptionTransform
{
    private const string DetailsRedactedMessage = "Detail redacted as it may contain sensitive data. " +
        "Specify 'Include Error Detail' in the connection string to include this information.";

    private const string StreamId = "streamid";
    private const string Version = "version";

    private static readonly Regex EventStreamUniqueExceptionDetailsRegex =
        new(@"\(stream_id, version\)=\((?<streamid>.*?), (?<version>\w+)\)");

    public bool TryTransform(Exception original, out Exception transformed)
    {
        if (!Matches(original))
        {
            transformed = null;
            return false;
        }

        var postgresException = original as PostgresException;

        object id = null;
        Type aggregateType = null;
        var expected = -1;
        var actual = -1;

        if (!string.IsNullOrEmpty(postgresException.Detail) && !postgresException.Detail.EqualsIgnoreCase(DetailsRedactedMessage))
        {
            var details = EventStreamUniqueExceptionDetailsRegex.Match(postgresException.Detail);

            if (details.Groups[StreamId].Success)
            {
                var streamId = details.Groups[StreamId].Value;

                id = Guid.TryParse(streamId, out var guidStreamId) ? guidStreamId : streamId;
            }

            if (details.Groups[Version].Success)
            {
                var actualVersion = details.Groups[Version].Value;

                if (int.TryParse(actualVersion, out var actualIntVersion))
                {
                    actual = actualIntVersion;
                    expected = actual - 1;
                }
            }

            transformed = new EventStreamUnexpectedMaxEventIdException(id, aggregateType, expected, actual);
            return true;
        }

        transformed = new EventStreamUnexpectedMaxEventIdException(postgresException.MessageText);
        return true;
    }

    private static bool Matches(Exception e)
    {
        return e is PostgresException pe
            && pe.SqlState == PostgresErrorCodes.UniqueViolation
            && (pe.ConstraintName == "pk_mt_events_stream_and_version" || pe.ConstraintName == "mt_events_default_stream_id_version_is_archived_idx");
    }
}
