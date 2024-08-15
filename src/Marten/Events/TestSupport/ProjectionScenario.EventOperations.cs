using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;

namespace Marten.Events.TestSupport;

public partial class ProjectionScenario
{
    public StreamAction Append(Guid stream, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null)
    {
        var step = action(e => e.Append(stream, events));
        if (events.Count() > 3)
        {
            step.Description = $"Append({stream}, events)";
        }
        else
        {
            step.Description = $"Append({stream}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Append(_store.Events, stream, events);
    }

    public StreamAction Append(Guid stream, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        var step = action(e => e.Append(stream, events));
        if (events.Count() > 3)
        {
            step.Description = $"Append({stream}, events)";
        }
        else
        {
            step.Description = $"Append({stream}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Append(_store.Events, stream, events);
    }

    public StreamAction Append(string stream, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null)
    {
        var step = action(e => e.Append(stream, events));
        if (events.Count() > 3)
        {
            step.Description = $"Append('{stream}', events)";
        }
        else
        {
            step.Description = $"Append('{stream}', {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Append(_store.Events, stream, events);
    }

    public StreamAction Append(string stream, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        var step = action(e => e.Append(stream, events));
        if (events.Count() > 3)
        {
            step.Description = $"Append('{stream}', events)";
        }
        else
        {
            step.Description = $"Append('{stream}', {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Append(_store.Events, stream, events);
    }

    public StreamAction Append(Guid stream, long expectedVersion, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        var step = action(e => e.Append(stream, expectedVersion,backfillTimestamp, events));
        if (events.Count() > 3)
        {
            step.Description = $"Append({stream}, {expectedVersion}, events)";
        }
        else
        {
            step.Description =
                $"Append({stream}, {expectedVersion}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Append(_store.Events, stream, events);
    }

    public StreamAction Append(string stream, long expectedVersion, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null)
    {
        var step = action(e => e.Append(stream, expectedVersion, events));
        if (events.Count() > 3)
        {
            step.Description = $"Append(\"{stream}\", {expectedVersion}, events)";
        }
        else
        {
            step.Description =
                $"Append(\"{stream}\", {expectedVersion}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Append(_store.Events, stream, events);
    }

    public StreamAction Append(string stream, long expectedVersion, DateTimeOffset? backfillTimestamp = null, params object[] events)
    {
        var step = action(e => e.Append(stream, expectedVersion, events));
        if (events.Count() > 3)
        {
            step.Description = $"Append(\"{stream}\", {expectedVersion}, events)";
        }
        else
        {
            step.Description =
                $"Append(\"{stream}\", {expectedVersion}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Append(_store.Events, stream, events);
    }

    public StreamAction StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class
    {
        var step = action(e => e.StartStream<TAggregate>(id, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream<{typeof(TAggregate).FullNameInCode()}>({id}, events)";
        }
        else
        {
            step.Description =
                $"StartStream<{typeof(TAggregate).FullNameInCode()}>({id}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, id, events);
    }

    public StreamAction StartStream(Type aggregateType, Guid id, IEnumerable<object> events)
    {
        var step = action(e => e.StartStream(aggregateType, id, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({aggregateType.FullNameInCode()}>({id}, events)";
        }
        else
        {
            step.Description =
                $"StartStream({aggregateType.FullNameInCode()}, {id}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, id, events);
    }

    public StreamAction StartStream(Type aggregateType, Guid id, params object[] events)
    {
        var step = action(e => e.StartStream(aggregateType, id, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({aggregateType.FullNameInCode()}>({id}, events)";
        }
        else
        {
            step.Description =
                $"StartStream({aggregateType.FullNameInCode()}, {id}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, id, events);
    }

    public StreamAction StartStream<TAggregate>(string streamKey, IEnumerable<object> events)
        where TAggregate : class
    {
        var step = action(e => e.StartStream<TAggregate>(streamKey, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream<{typeof(TAggregate).FullNameInCode()}>(\"{streamKey}\", events)";
        }
        else
        {
            step.Description =
                $"StartStream<{typeof(TAggregate).FullNameInCode()}>(\"{streamKey}\", {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamKey, events);
    }

    public StreamAction StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class
    {
        var step = action(e => e.StartStream<TAggregate>(streamKey, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream<{typeof(TAggregate).FullNameInCode()}>(\"{streamKey}\", events)";
        }
        else
        {
            step.Description =
                $"StartStream<{typeof(TAggregate).FullNameInCode()}>(\"{streamKey}\", {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamKey, events);
    }

    public StreamAction StartStream(Type aggregateType, string streamKey, IEnumerable<object> events)
    {
        var step = action(e => e.StartStream(aggregateType, streamKey, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({aggregateType.FullNameInCode()}>(\"{streamKey}\", events)";
        }
        else
        {
            step.Description =
                $"StartStream({aggregateType.FullNameInCode()}, \"{streamKey}\", {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamKey, events);
    }

    public StreamAction StartStream(Type aggregateType, string streamKey, params object[] events)
    {
        var step = action(e => e.StartStream(aggregateType, streamKey, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({aggregateType.FullNameInCode()}>(\"{streamKey}\", events)";
        }
        else
        {
            step.Description =
                $"StartStream({aggregateType.FullNameInCode()}, \"{streamKey}\", {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamKey, events);
    }

    public StreamAction StartStream(Guid id, IEnumerable<object> events)
    {
        var step = action(e => e.StartStream(id, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({id}, events)";
        }
        else
        {
            step.Description = $"StartStream({id}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, id, events);
    }

    public StreamAction StartStream(Guid id, params object[] events)
    {
        var step = action(e => e.StartStream(id, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({id}, events)";
        }
        else
        {
            step.Description = $"StartStream({id}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, id, events);
    }

    public StreamAction StartStream(string streamKey, IEnumerable<object> events)
    {
        var step = action(e => e.StartStream(streamKey, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream(\"{streamKey}\", events)";
        }
        else
        {
            step.Description = $"StartStream(\"{streamKey}\", {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamKey, events);
    }

    public StreamAction StartStream(string streamKey, params object[] events)
    {
        var step = action(e => e.StartStream(streamKey, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream(\"{streamKey}\", events)";
        }
        else
        {
            step.Description = $"StartStream(\"{streamKey}\", {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamKey, events);
    }

    public StreamAction StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class
    {
        var streamId = Guid.NewGuid();
        var step = action(e => e.StartStream<TAggregate>(streamId, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream<{typeof(TAggregate).FullNameInCode()}>(events)";
        }
        else
        {
            step.Description =
                $"StartStream<{typeof(TAggregate).FullNameInCode()}>({events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamId, events);
    }

    public StreamAction StartStream<TAggregate>(params object[] events) where TAggregate : class
    {
        var streamId = Guid.NewGuid();
        var step = action(e => e.StartStream<TAggregate>(streamId, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream<{typeof(TAggregate).FullNameInCode()}>(events)";
        }
        else
        {
            step.Description =
                $"StartStream<{typeof(TAggregate).FullNameInCode()}>({events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamId, events);
    }

    public StreamAction StartStream(Type aggregateType, IEnumerable<object> events)
    {
        var streamId = Guid.NewGuid();
        var step = action(e => e.StartStream(aggregateType, streamId, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({aggregateType.FullNameInCode()}>(events)";
        }
        else
        {
            step.Description =
                $"StartStream({aggregateType.FullNameInCode()}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamId, events);
    }

    public StreamAction StartStream(Type aggregateType, params object[] events)
    {
        var streamId = Guid.NewGuid();
        var step = action(e => e.StartStream(aggregateType, streamId, events));
        if (events.Count() > 3)
        {
            step.Description = $"StartStream({aggregateType.FullNameInCode()}>(events)";
        }
        else
        {
            step.Description =
                $"StartStream({aggregateType.FullNameInCode()}, {events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamId, events);
    }

    public StreamAction StartStream(IEnumerable<object> events)
    {
        var streamId = Guid.NewGuid();
        var step = action(e => e.StartStream(streamId, events));
        if (events.Count() > 3)
        {
            step.Description = "StartStream(events)";
        }
        else
        {
            step.Description = $"StartStream({events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamId, events);
    }

    public StreamAction StartStream(params object[] events)
    {
        var streamId = Guid.NewGuid();
        var step = action(e => e.StartStream(streamId, events));
        if (events.Count() > 3)
        {
            step.Description = "StartStream(events)";
        }
        else
        {
            step.Description = $"StartStream({events.Select(x => x.ToString()).Join(", ")})";
        }

        return StreamAction.Start(_store.Events, streamId, events);
    }

    /// <summary>
    ///     Make any number of append event operations in the scenario sequence
    /// </summary>
    /// <param name="description">Descriptive explanation of the action in case of failures</param>
    /// <param name="appendAction"></param>
    public void AppendEvents(string description, Action<IEventOperations> appendAction)
    {
        action(appendAction).Description = description;
    }

    /// <summary>
    ///     Make any number of append event operations in the scenario sequence
    /// </summary>
    /// <param name="appendAction"></param>
    public void AppendEvents(Action<IEventOperations> appendAction)
    {
        AppendEvents("Appending events...", appendAction);
    }
}
