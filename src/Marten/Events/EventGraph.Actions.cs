using System;
using System.Linq;
using JasperFx.Core;
using Marten.Internal.Sessions;

namespace Marten.Events;

public partial class EventGraph
{
    internal StreamAction Append(DocumentSessionBase session, Guid stream, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        EnsureAsGuidStorage(session);

        if (stream == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(stream), "Cannot use an empty Guid as the stream id");
        }

        var wrapped = events.Select(o =>
        {
            var e = BuildEvent(o);
            e.StreamId = stream;
            return e;
        }).ToArray();

        if (backfillTimestamp is not null)
        {
            wrapped.Each(x => x.Timestamp = backfillTimestamp.Value);
        }

        if (session.WorkTracker.TryFindStream(stream, out var eventStream))
        {
            eventStream.AddEvents(wrapped);
        }
        else
        {
            eventStream = StreamAction.Append(stream, wrapped);
            eventStream.TenantId = session.TenantId;

            session.WorkTracker.Streams.Add(eventStream);
        }

        return eventStream;
    }

    internal StreamAction Append(DocumentSessionBase session, string stream, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        EnsureAsStringStorage(session);

        if (stream.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(nameof(stream), "The stream key cannot be null or empty");
        }

        var wrapped = events.Select(o =>
        {
            var e = BuildEvent(o);
            e.StreamKey = stream;
            return e;
        }).ToArray();

        if (backfillTimestamp is not null)
        {
            wrapped.Each(x => x.Timestamp = backfillTimestamp.Value);
        }

        if (session.WorkTracker.TryFindStream(stream, out var eventStream))
        {
            eventStream.AddEvents(wrapped);
        }
        else
        {
            eventStream = StreamAction.Append(stream, wrapped);
            eventStream.TenantId = session.TenantId;
            session.WorkTracker.Streams.Add(eventStream);
        }

        return eventStream;
    }

    internal StreamAction StartStream(DocumentSessionBase session, Guid id, params object[] events)
    {
        EnsureAsGuidStorage(session);

        if (id == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Cannot use an empty Guid as the stream id");
        }


        var stream = StreamAction.Start(this, id, events);
        stream.TenantId = session.TenantId;
        session.WorkTracker.Streams.Add(stream);

        return stream;
    }

    internal StreamAction StartEmptyStream(DocumentSessionBase session, Guid id, params object[] events)
    {
        EnsureAsGuidStorage(session);

        if (id == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Cannot use an empty Guid as the stream id");
        }


        var stream = new StreamAction(id, StreamActionType.Start) { TenantId = session.TenantId };

        session.WorkTracker.Streams.Add(stream);

        return stream;
    }

    internal StreamAction StartEmptyStream(DocumentSessionBase session, string key, params object[] events)
    {
        EnsureAsStringStorage(session);

        if (key.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(nameof(key), "Cannot use an empty or null string as the stream key");
        }


        var stream = new StreamAction(key, StreamActionType.Start) { TenantId = session.TenantId };

        session.WorkTracker.Streams.Add(stream);

        return stream;
    }

    internal StreamAction StartStream(DocumentSessionBase session, string streamKey, params object[] events)
    {
        EnsureAsStringStorage(session);

        if (streamKey.IsEmpty())
        {
            throw new ArgumentOutOfRangeException(nameof(streamKey), "The stream key cannot be null or empty");
        }


        var stream = StreamAction.Start(this, streamKey, events);
        stream.TenantId = session.TenantId;

        session.WorkTracker.Streams.Add(stream);

        return stream;
    }
}
