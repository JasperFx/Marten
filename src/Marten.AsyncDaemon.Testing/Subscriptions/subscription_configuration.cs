using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Daemon.Internals;
using Marten.Events.Projections;
using Marten.Services;
using Marten.Subscriptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.AsyncDaemon.Testing.Subscriptions;

public class subscription_configuration : OneOffConfigurationsContext
{
    [Fact]
    public void register_subscription_and_part_of_shards()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Subscribe(new FakeSubscription());
        });

        theStore.Options.Projections.AllShards().Select(x => x.Name.Identity)
            .ShouldContain("Fake:All");

    }

    [Fact]
    public async Task start_up_the_subscription()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Subscribe(new FakeSubscription());
        });

        using var daemon = await theStore.BuildProjectionDaemonAsync();
        await daemon.StartAgentAsync("Fake:All", CancellationToken.None);
    }

    [Fact]
    public void validate_on_uniqueness_of_shard_names_with_subscriptions_and_projections()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Subscribe(new FakeSubscription());
            opts.Projections.Add(new FakeProjection(), ProjectionLifecycle.Async, projectionName: "Fake");
        });

        theStore.ShouldNotBeNull();
    }
}

public class FakeProjection: IProjection
{
    public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
    {
        throw new System.NotImplementedException();
    }

    public async Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams, CancellationToken cancellation)
    {
        throw new System.NotImplementedException();
    }
}

public class FakeSubscription: SubscriptionBase
{
    public FakeSubscription()
    {
        SubscriptionName = "Fake";
    }

    public List<IEvent> EventsEncountered { get; } = new List<IEvent>();

    public override Task ProcessEventsAsync(EventRange page, IDocumentOperations operations, CancellationToken cancellationToken)
    {
        page.Listeners.Add(Listener);
        EventsEncountered.AddRange(page.Events);
        return Task.CompletedTask;
    }

    public FakeChangeListener Listener { get; } = new();
}

public class FakeChangeListener: IChangeListener
{
    public Task AfterCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        AfterCommitWasCalled = true;
        return Task.CompletedTask;
    }

    public bool AfterCommitWasCalled { get; set; }

    public Task BeforeCommitAsync(IDocumentSession session, IChangeSet commit, CancellationToken token)
    {
        BeforeCommitWasCalled = true;
        return Task.CompletedTask;
    }

    public bool BeforeCommitWasCalled { get; set; }
}
