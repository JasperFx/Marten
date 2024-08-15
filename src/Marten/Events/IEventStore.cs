#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Events;

public interface IEventStore: IEventOperations, IQueryEventStore
{
    /// <summary>
    ///     Append one or more events in order to an existing stream and verify that maximum event id for the stream
    ///     matches supplied expected version or transaction is aborted.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="expectedVersion">Expected maximum event version after append</param>
    /// <param name="events"></param>
    StreamAction Append(Guid stream, long expectedVersion, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(Guid id, IEnumerable<object> events) where TAggregate : class;

    /// <summary>
    ///     Append events to an existing stream with optimistic concurrency checks against the
    ///     existing version of the stream
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="token"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendOptimistic(string streamKey, CancellationToken token, params object[] events);

    /// <summary>
    ///     Append events to an existing stream with optimistic concurrency checks against the
    ///     existing version of the stream
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendOptimistic(string streamKey, params object[] events);

    /// <summary>
    ///     Append events to an existing stream with optimistic concurrency checks against the
    ///     existing version of the stream
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="token"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendOptimistic(Guid streamId, CancellationToken token, params object[] events);

    /// <summary>
    ///     Append events to an existing stream with optimistic concurrency checks against the
    ///     existing version of the stream
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendOptimistic(Guid streamId, params object[] events);

    /// <summary>
    ///     Append events to an existing stream with an exclusive lock against the
    ///     stream until this session is saved
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="token"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendExclusive(string streamKey, CancellationToken token, params object[] events);

    /// <summary>
    ///     Append events to an existing stream with an exclusive lock against the
    ///     stream until this session is saved
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendExclusive(string streamKey, params object[] events);

    /// <summary>
    ///     Append events to an existing stream with an exclusive lock against the
    ///     stream until this session is saved
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendExclusive(Guid streamId, CancellationToken token, params object[] events);

    /// <summary>
    ///     Append events to an existing stream with an exclusive lock against the
    ///     stream until this session is saved
    /// </summary>
    /// <param name="streamId"></param>
    /// <param name="events"></param>
    /// <exception cref="NonExistentStreamException"></exception>
    /// <returns></returns>
    Task AppendExclusive(Guid streamId, params object[] events);

    /// <summary>
    ///     Mark a stream and all its events as archived
    /// </summary>
    /// <param name="streamId"></param>
    void ArchiveStream(Guid streamId);

    /// <summary>
    ///     Mark a stream and all its events as archived
    /// </summary>
    /// <param name="streamKey"></param>
    void ArchiveStream(string streamKey);


    /// <summary>
    ///     Fetch the projected aggregate T by id with built in optimistic concurrency checks
    ///     starting at the point the aggregate was fetched.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(Guid id, CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(Guid id, Action<IEventStream<T>> writing, CancellationToken cancellation = default)
        where T : class;

    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(Guid id, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default)
        where T : class;

    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(string id, Action<IEventStream<T>> writing, CancellationToken cancellation = default)
        where T : class;

    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(string id, Func<IEventStream<T>, Task> writing, CancellationToken cancellation = default)
        where T : class;


    /// <summary>
    ///     Fetch the projected aggregate T by id with built in optimistic concurrency checks
    ///     starting at the point the aggregate was fetched.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(string key, CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Fetch projected aggregate T by id and expected, current version of the aggregate. Will fail immediately
    ///     with ConcurrencyInjection if the expectedVersion is stale. Builds in optimistic concurrency for later
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(Guid id, long expectedVersion, CancellationToken cancellation = default)
        where T : class;

    /// <summary>
    ///     Fetch projected aggregate T by id and expected, current version of the aggregate. Will fail immediately
    ///     with ConcurrencyInjection if the expectedVersion is stale. Builds in optimistic concurrency for later
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForWriting<T>(string key, long expectedVersion, CancellationToken cancellation = default)
        where T : class;


    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(Guid id, int expectedVersion, Action<IEventStream<T>> writing,
        CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(Guid id, int expectedVersion, Func<IEventStream<T>, Task> writing,
        CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(string id, int expectedVersion, Action<IEventStream<T>> writing,
        CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Conditionally write to an event stream for the current version of the aggregate of type T
    ///     This automatically persists the entire session
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteToAggregate<T>(string id, int expectedVersion, Func<IEventStream<T>, Task> writing,
        CancellationToken cancellation = default) where T : class;


    /// <summary>
    ///     Fetch projected aggregate T by id for exclusive writing
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForExclusiveWriting<T>(Guid id, CancellationToken cancellation = default)
        where T : class;

    /// <summary>
    ///     Fetch projected aggregate T by id for exclusive writing
    /// </summary>
    /// <param name="id"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IEventStream<T>> FetchForExclusiveWriting<T>(string key, CancellationToken cancellation = default)
        where T : class;

    /// <summary>
    ///     Write exclusively to the stream for aggregate of type T. This can time out if it is unable
    ///     to attain a lock on the stream in time
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteExclusivelyToAggregate<T>(Guid id, Action<IEventStream<T>> writing,
        CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Write exclusively to the stream for aggregate of type T. This can time out if it is unable
    ///     to attain a lock on the stream in time
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteExclusivelyToAggregate<T>(string id, Action<IEventStream<T>> writing,
        CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Write exclusively to the stream for aggregate of type T. This can time out if it is unable
    ///     to attain a lock on the stream in time
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteExclusivelyToAggregate<T>(Guid id, Func<IEventStream<T>, Task> writing,
        CancellationToken cancellation = default) where T : class;

    /// <summary>
    ///     Write exclusively to the stream for aggregate of type T. This can time out if it is unable
    ///     to attain a lock on the stream in time
    /// </summary>
    /// <param name="id"></param>
    /// <param name="expectedVersion">The starting version of the aggregate for optimistic concurrency checks</param>
    /// <param name="writing"></param>
    /// <param name="cancellation"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task WriteExclusivelyToAggregate<T>(string id, Func<IEventStream<T>, Task> writing,
        CancellationToken cancellation = default) where T : class;
}
