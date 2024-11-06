#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Events;

namespace Marten.Events.Aggregation;

public interface IEventSlicer<TDoc, TId>
{
    /// <summary>
    ///     This is called by the asynchronous projection runner
    /// </summary>
    /// <param name="querySession"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, TId>>> SliceAsyncEvents(IQuerySession querySession,
        List<IEvent> events);
}
