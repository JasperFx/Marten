#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Aggregation;

public interface ISingleStreamSlicer{}

/// <summary>
///     Slicer strategy by stream id (Guid identified streams)
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamId<TDoc>: IEventSlicer<TDoc, Guid>, ISingleStreamSlicer
{
    public ValueTask<IReadOnlyList<EventSlice<TDoc, Guid>>> SliceInlineActions(IQuerySession querySession,
        IEnumerable<StreamAction> streams)
    {
        return new ValueTask<IReadOnlyList<EventSlice<TDoc, Guid>>>(streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, Guid>(s.Id, tenant, s.Events){ActionType = s.ActionType};
        }).ToList());
    }


    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, Guid>>> SliceAsyncEvents(IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<TenantSliceGroup<TDoc, Guid>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamId)
                .Select(x => new EventSlice<TDoc, Guid>(x.Key, tenant, x));

            var group = new TenantSliceGroup<TDoc, Guid>(tenant, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, Guid>>>(list);
    }
}
