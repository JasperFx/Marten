using System;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests.Aggregation;

public class when_inline_event_enriched: OneOffConfigurationsContext
{
    [Fact]
    public async Task async_apply_with_enrich_event()
    {
        StoreOptions(o =>
        {
            o.Listeners.Add(new DocumentEnricher());
            o.Projections.Snapshot<User>(SnapshotLifecycle.Inline);
        });

        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new EnrichedUser());
        await theSession.SaveChangesAsync();

        var aggregate = await theSession.Query<User>().FirstAsync();
        aggregate.Actor.ShouldBe("some user from identity");
    }

    public class DocumentEnricher: DocumentSessionListenerBase
    {
        public override void BeforeProcessChanges(IDocumentSession session)
        {
            var streams = session.PendingChanges.Streams();
            foreach (var stream in streams)
            {
                foreach (var e in stream.Events)
                {
                    if (e.Data is EnrichedUserEvent eueb)
                    {
                        eueb.Actor = "some user from identity";
                    }
                }
            }
        }

        public override Task BeforeProcessChangesAsync(IDocumentSession session, CancellationToken token)
        {
            return Task.Factory.StartNew(() => BeforeProcessChanges(session));
        }
    }

    public record User
    {
        public Guid Id { get; set; }
        public string Actor { get; set; }
        private void Apply(EnrichedUser e) => Actor = e.Actor;
    }

    public record EnrichedUser: EnrichedUserEvent;

    public abstract record EnrichedUserEvent
    {
        public string Actor { get; set; }
    }
}
