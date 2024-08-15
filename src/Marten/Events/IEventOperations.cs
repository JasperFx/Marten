using System;
using System.Collections.Generic;

namespace Marten.Events;

public interface IEventOperations
{
    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(Guid stream, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null);

    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(Guid stream, DateTimeOffset? backfillTimestamp = null, params object[] events);

    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(string stream, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null);

    /// <summary>
    ///     Append one or more events in order to an existing stream
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="events"></param>
    StreamAction Append(string stream, DateTimeOffset? backfillTimestamp = null, params object[] events);

    /// <summary>
    ///     Append one or more events in order to an existing stream and verify that maximum event id for the stream
    ///     matches supplied expected version or transaction is aborted.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="expectedVersion">Expected maximum event version after append</param>
    /// <param name="events"></param>
    StreamAction Append(Guid stream, long expectedVersion, DateTimeOffset? backfillTimestamp = null, params object[] events);

    /// <summary>
    ///     Append one or more events in order to an existing stream and verify that maximum event id for the stream
    ///     matches supplied expected version or transaction is aborted.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="expectedVersion">Expected maximum event version after append</param>
    /// <param name="events"></param>
    StreamAction Append(string stream, long expectedVersion, IEnumerable<object> events, DateTimeOffset? backfillTimestamp = null);

    /// <summary>
    ///     Append one or more events in order to an existing stream and verify that maximum event id for the stream
    ///     matches supplied expected version or transaction is aborted.
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="expectedVersion">Expected maximum event version after append</param>
    /// <param name="events"></param>
    StreamAction Append(string stream, long expectedVersion, DateTimeOffset? backfillTimestamp = null, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(Guid id, params object[] events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, Guid id, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, Guid id, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(string streamKey, IEnumerable<object> events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(string streamKey, params object[] events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, string streamKey, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="aggregateType"></param>
    /// <param name="streamKey">String identifier of this stream</param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, string streamKey, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream - WILL
    ///     THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Guid id, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream - WILL
    ///     THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="id"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Guid id, params object[] events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(string streamKey, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream based on a user-supplied Guid and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <param name="streamKey"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(string streamKey, params object[] events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(IEnumerable<object> events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream<TAggregate>(params object[] events) where TAggregate : class;

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(Type aggregateType, params object[] events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(IEnumerable<object> events);

    /// <summary>
    ///     Creates a new event stream, assigns a new Guid id, and appends the events in order to the new stream
    ///     - WILL THROW AN EXCEPTION IF THE STREAM ALREADY EXISTS
    /// </summary>
    /// <typeparam name="TAggregate"></typeparam>
    /// <param name="events"></param>
    /// <returns></returns>
    StreamAction StartStream(params object[] events);
}
