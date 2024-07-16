#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.Events.Aggregation;

/// <summary>
///     Slicer strategy by stream key (string identified streams)
/// </summary>
/// <typeparam name="TDoc"></typeparam>
public class ByStreamKey<TDoc>: IEventSlicer<TDoc, string>, ISingleStreamSlicer
{
    public ValueTask<IReadOnlyList<EventSlice<TDoc, string>>> SliceInlineActions(IQuerySession querySession,
        IEnumerable<StreamAction> streams)
    {
        return new ValueTask<IReadOnlyList<EventSlice<TDoc, string>>>(streams.Select(s =>
        {
            var tenant = new Tenant(s.TenantId, querySession.Database);
            return new EventSlice<TDoc, string>(s.Key!, tenant, s.Events){ActionType = s.ActionType};
        }).ToList());
    }

    public ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, string>>> SliceAsyncEvents(
        IQuerySession querySession,
        List<IEvent> events)
    {
        var list = new List<TenantSliceGroup<TDoc, string>>();
        var byTenant = events.GroupBy(x => x.TenantId);

        foreach (var tenantGroup in byTenant)
        {
            var tenant = new Tenant(tenantGroup.Key, querySession.Database);

            var slices = tenantGroup
                .GroupBy(x => x.StreamKey)
                .Select(x => new EventSlice<TDoc, string>(x.Key!, tenant, x));

            var group = new TenantSliceGroup<TDoc, string>(tenant, slices);

            list.Add(group);
        }

        return new ValueTask<IReadOnlyList<TenantSliceGroup<TDoc, string>>>(list);
    }
}
