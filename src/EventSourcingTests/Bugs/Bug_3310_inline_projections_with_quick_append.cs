using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Metadata;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace EventSourcingTests.Bugs;

public class Bug_3310_inline_projections_with_quick_append : BugIntegrationContext
{
    private readonly ITestOutputHelper _testOutputHelper;

    // For load testing, I was using 20 iterations
    private const int Iterations = 3;
    // For load testing, I was using 1000 for NSize
    private const int NSize = 10;

    private const string tenant = "tenant-1";

    public Bug_3310_inline_projections_with_quick_append(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        StoreOptions(opts =>
        {
            opts.Events.AppendMode = EventAppendMode.Quick;
            opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.Policies.AllDocumentsAreMultiTenanted();
            // every opportunity to make things work
            opts.Events.AddEventType<LoadTestEvent>();
            opts.Events.AddEventType<LoadTestUnrelatedEvent>();
            opts.Projections.Snapshot<LoadTestInlineProjection>(SnapshotLifecycle.Inline);
            opts.Projections.Snapshot<LoadTestUnrelatedInlineProjection>(SnapshotLifecycle.Inline);

            // opts.SetApplicationProject(GetType().Assembly);
            // opts.GeneratedCodeMode = TypeLoadMode.Auto;
            // opts.GeneratedCodeOutputPath =
            //     AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory().AppendPath("Internal", "Generated");

        });
    }

    [Fact]
    public async Task start_and_append_events_to_same_stream()
    {
        await using var session = theStore.LightweightSession(tenant);

        session.Logger = new TestOutputMartenLogger(_testOutputHelper);

        var streamId = Guid.NewGuid().ToString();

        session.Events.StartStream<LoadTestInlineProjection>(streamId,new LoadTestEvent(Guid.NewGuid(), 1),
            new LoadTestEvent(Guid.NewGuid(), 2), new LoadTestEvent(Guid.NewGuid(), 3));
        await session.SaveChangesAsync();

        _testOutputHelper.WriteLine("APPEND STARTS HERE");

        session.Events.Append(streamId, new LoadTestEvent(Guid.NewGuid(), 4), new LoadTestEvent(Guid.NewGuid(), 5));
        await session.SaveChangesAsync();

        var doc = await session.LoadAsync<LoadTestInlineProjection>(streamId);
        doc.Version.ShouldBe(5);


    }

    [Fact]
    public async Task create_1_stream_with_many_events()
    {
        await using var session = theStore.LightweightSession(tenant);

        session.Logger = new TestOutputMartenLogger(_testOutputHelper);

        await Preload(session);

        var sw = new Stopwatch();
        foreach (var iteration in Enumerable.Range(1, Iterations))
        {
            var events = Enumerable.Range(1, NSize).Select(i => new LoadTestEvent(Guid.NewGuid(), i));

            var streamKey = CombGuidIdGeneration.NewGuid().ToString();
            session.Events.StartStream(streamKey, events.ToList());

            sw.Restart();
            await session.SaveChangesAsync();
            _testOutputHelper.WriteLine($"{iteration:D3}: {sw.Elapsed:g}");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task update_1_stream_with_many_events(bool appendWithExpectedVersion)
    {
        await using var session = theStore.LightweightSession(tenant);
        await Preload(session);

        var streamKey = CombGuidIdGeneration.NewGuid().ToString();
        var eventCount = 1;
        session.Events.StartStream(streamKey, new LoadTestEvent(Guid.NewGuid(), eventCount));
        await session.SaveChangesAsync();

        var sw = new Stopwatch();
        foreach (var iteration in Enumerable.Range(1, Iterations))
        {
            var events = Enumerable.Range(eventCount + 1, NSize).Select(i => new LoadTestEvent(Guid.NewGuid(), i)).ToList();
            eventCount += NSize;

            if (appendWithExpectedVersion)
            {
                session.Events.Append(streamKey, eventCount, events);
            }
            else
            {
                session.Events.Append(streamKey, events);
            }

            sw.Restart();
            await session.SaveChangesAsync();
            _testOutputHelper.WriteLine($"{iteration:D3}: {sw.Elapsed:g}");
        }


        // verify
        var result = await session.LoadAsync<LoadTestInlineProjection>(streamKey);

        result.ShouldNotBeNull();
        // Here is where the bug is shown - caused by the version on the inline projection not being accurate as it takes the revision from the version of the last event.. which is not accurate in quickappend
        result.Version.ShouldBe(eventCount);
        result.Sum.ShouldBe(Sum1ToN(eventCount));
    }

    [Fact]
    public async Task create_many_streams_with_1_event()
    {
        await using var session = theStore.LightweightSession(tenant);
        await Preload(session);

        var sw = new Stopwatch();
        foreach (var iteration in Enumerable.Range(1, Iterations))
        {
            foreach (var i in Enumerable.Range(1, NSize))
            {
                var streamKey = CombGuidIdGeneration.NewGuid().ToString();
                session.Events.StartStream(streamKey, new LoadTestEvent(Guid.NewGuid(), i));
            }

            sw.Restart();
            await session.SaveChangesAsync();
            _testOutputHelper.WriteLine($"{iteration:D3}: {sw.Elapsed:g}");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task update_many_streams_with_1_event(bool appendWithExpectedVersion)
    {
        await using var session = theStore.LightweightSession(tenant);
        await Preload(session);

        var streamKeys = new string[NSize];
        foreach (var i in Enumerable.Range(1, NSize))
        {
            streamKeys[i - 1] = CombGuidIdGeneration.NewGuid().ToString();
            session.Events.StartStream(streamKeys[i - 1], new LoadTestEvent(Guid.NewGuid(), 0));
        }

        await session.SaveChangesAsync();

        var sw = new Stopwatch();
        foreach (var iteration in Enumerable.Range(1, Iterations))
        {
            foreach (var i in Enumerable.Range(1, NSize))
            {
                if (appendWithExpectedVersion)
                {
                    session.Events.Append(streamKeys[i - 1], iteration + 1, new LoadTestEvent(Guid.NewGuid(), iteration));
                }
                else
                {
                    session.Events.Append(streamKeys[i - 1], new LoadTestEvent(Guid.NewGuid(), iteration));
                }
            }

            sw.Restart();
            await session.SaveChangesAsync();
            _testOutputHelper.WriteLine($"{iteration:D3}: {sw.Elapsed:g}");
        }

        // verify
        var result = await session.LoadAsync<LoadTestInlineProjection>(streamKeys[0]);

        result.ShouldNotBeNull();
        result.Version.ShouldBe(Iterations + 1);
        result.Sum.ShouldBe(Sum1ToN(Iterations));
    }

    private static long Sum1ToN(long n) => n * (n + 1) / 2;

    private static Task Preload(IDocumentSession session)
    {
        session.Events.StartStream(CombGuidIdGeneration.NewGuid().ToString(),
            new LoadTestEvent(Guid.NewGuid(), 0),
            new LoadTestUnrelatedEvent());
        return session.SaveChangesAsync();
    }

    public record LoadTestEvent(Guid Value, int Count);
    public record LoadTestUnrelatedEvent;

    [DocumentAlias("load_testing_inline_projection")]
    public record LoadTestInlineProjection : IRevisioned
    {
        [Identity]
        public string StreamKey { get; init; }
        public Guid LastValue { get; init; }
        public long Sum { get; init; }
        //[Version]
        public int Version { get; set; }

        public LoadTestInlineProjection Apply(LoadTestEvent @event, LoadTestInlineProjection current)
        {
            return current with { LastValue = @event.Value, Sum = current.Sum + @event.Count };
        }
    }

    [DocumentAlias("load_testing_unrelated_inline_projection")]
    public record LoadTestUnrelatedInlineProjection
    {
        [Identity]
        public string StreamKey { get; init; }
        public long Count { get; init; }
        [Version]
        public int Version { get; set; }

        public LoadTestUnrelatedInlineProjection Apply(LoadTestUnrelatedEvent @event, LoadTestUnrelatedInlineProjection current)
        {
            return current with { Count = current.Count + 1 };
        }
    }
}
