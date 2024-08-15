#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Marten.Events;

internal partial class EventStore
{
    public StreamAction Append(Guid stream, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null)
    {
        //TODO NRT: We're ignoring null here as to not unintentionally change any downstream behaviour - Replace with null guards in the future.
        return Append(stream, backfillTimestamp, events?.ToArray()!);
    }

    public StreamAction Append(Guid stream, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        return _store.Events.Append(_session, stream,backfillTimestamp, events);
    }

    public StreamAction Append(string stream, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null)
    {
        return Append(stream, backfillTimestamp, events?.ToArray()!);
    }

    public StreamAction Append(string stream, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        return _store.Events.Append(_session, stream,backfillTimestamp, events);
    }

    public StreamAction Append(Guid stream, long expectedVersion, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null)
    {
        return Append(stream, expectedVersion, backfillTimestamp, events?.ToArray()!);
    }

    public StreamAction Append(Guid stream, long expectedVersion, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        var eventStream = Append(stream, events);
        eventStream.ExpectedVersionOnServer = expectedVersion - eventStream.Events.Count;

        if (eventStream.ExpectedVersionOnServer < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedVersion),
                "The expected version cannot be less than the number of events being appended");

        return eventStream;
    }

    public StreamAction Append(string stream, long expectedVersion, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null)
    {
        return Append(stream, expectedVersion, backfillTimestamp, events?.ToArray()!);
    }

    public StreamAction Append(string stream, long expectedVersion, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        var eventStream = Append(stream, events, backfillTimestamp);
        eventStream.ExpectedVersionOnServer = expectedVersion - eventStream.Events.Count;

        return eventStream;
    }
}
