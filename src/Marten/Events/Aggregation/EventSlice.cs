#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Events.Aggregation;
using Marten.Storage;

namespace Marten.Events.Projections;

public interface IEventSlice
{
    Tenant Tenant { get; }
    IReadOnlyList<IEvent> Events();
}

/// <summary>
///     A grouping of events that will be applied to an aggregate of type TDoc
///     with the identity TId
/// </summary>
/// <typeparam name="TDoc"></typeparam>
/// <typeparam name="TId"></typeparam>
public class EventSlice<TDoc, TId>: IEventSlice, IComparer<IEvent>
{
    private readonly List<IEvent> _events = new();

    public EventSlice(TId id, Tenant tenant, IEnumerable<IEvent>? events = null)
    {
        Id = id;
        Tenant = tenant;
        if (events != null)
        {
            _events.AddRange(events);
        }
    }

    public EventSlice(TId id, IQuerySession querySession, IEnumerable<IEvent>? events = null): this(id,
        new Tenant(Tenancy.DefaultTenantId, querySession.Database), events)
    {
    }

    private readonly StreamActionType? _actionType;

    /// <summary>
    ///     Is this action the start of a new stream or appending
    ///     to an existing stream?
    /// </summary>
    /// <remarks>
    ///     Default's to determining from the version of the first event on
    ///     stream, but can be overridden so that the value works with
    ///     QuickAppend
    /// </remarks>
    public StreamActionType ActionType
    {
        get => _actionType ?? (_events[0].Version == 1 ? StreamActionType.Start : StreamActionType.Append);
        init => _actionType = value;
    }

    /// <summary>
    ///     The aggregate identity
    /// </summary>
    public TId Id { get; }

    /// <summary>
    ///     The current tenant
    /// </summary>
    public Tenant Tenant { get; }

    /// <summary>
    ///     The related aggregate document
    /// </summary>
    public TDoc? Aggregate { get; set; }

    public int Count => _events.Count;

    int IComparer<IEvent>.Compare(IEvent x, IEvent y)
    {
        return x.Sequence.CompareTo(y.Sequence);
    }

    /// <summary>
    ///     All the events in this slice
    /// </summary>
    public IReadOnlyList<IEvent> Events()
    {
        return _events;
    }

    /// <summary>
    ///     Add a single event to this slice
    /// </summary>
    /// <param name="e"></param>
    public void AddEvent(IEvent e)
    {
        _events.Add(e);
    }

    /// <summary>
    ///     Add a grouping of events to this slice
    /// </summary>
    /// <param name="events"></param>
    public void AddEvents(IEnumerable<IEvent> events)
    {
        _events.AddRange(events);
    }

    /// <summary>
    ///     Iterate through just the event data
    /// </summary>
    /// <returns></returns>
    public IEnumerable<object> AllData()
    {
        foreach (var @event in _events) yield return @event.Data;
    }

    internal void FanOut<TSource, TChild>(Func<TSource, IEnumerable<TChild>> fanOutFunc)
    {
        reorderEvents();
        _events.FanOut(fanOutFunc);
    }

    internal void ApplyFanOutRules(IEnumerable<IFanOutRule> rules)
    {
        // Need to do this first before applying the fanout rules
        reorderEvents();

        foreach (var rule in rules) rule.Apply(_events);
    }

    private void reorderEvents()
    {
        var events = _events.Distinct().OrderBy(x => x.Sequence).ToArray();
        _events.Clear();
        _events.AddRange(events);
    }
}
