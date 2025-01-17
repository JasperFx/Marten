using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace EventSourcingTests;

public class EventStreamUnexpectedMaxEventIdExceptionTransformTest: IntegrationContext
{
    public EventStreamUnexpectedMaxEventIdExceptionTransformTest(DefaultStoreFixture fixture):
        base(fixture)
    {
    }

    [Fact(Skip = "TODO -- too unreliable on CI")]
    public async Task throw_transformed_exception_with_details_redacted()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var streamId = Guid.NewGuid();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };
        theSession.Events.StartStream<Quest>(streamId, joined);
        theSession.Events.Append(streamId, departed);
        await theSession.SaveChangesAsync();

        var forceEventStreamUnexpectedMaxEventIdException = async () =>
        {
            await Parallel.ForEachAsync(Enumerable.Range(1, 10), async (_, token) =>
            {
                await using var session = theStore.LightweightSession();
                session.Events.Append(streamId, departed);
                await session.SaveChangesAsync(token);
            });
        };

        (await Should.ThrowAsync<EventStreamUnexpectedMaxEventIdException>(forceEventStreamUnexpectedMaxEventIdException))
            .Message.ShouldContain("pk_mt_events_stream_and_version");
    }

    [Fact(Skip = "TODO -- too unreliable on CI")]
    public async Task throw_transformed_exception_with_details_available()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var connectionString = ConnectionSource.ConnectionString + ";Include Error Detail=true";
        StoreOptions(storeOptions => storeOptions.Connection(connectionString));

        var streamId = Guid.NewGuid();
        var joined = new MembersJoined { Members = new[] { "Rand", "Matt", "Perrin", "Thom" } };
        var departed = new MembersDeparted { Members = new[] { "Thom" } };
        theSession.Events.StartStream<Quest>(streamId, joined);
        theSession.Events.Append(streamId, departed);
        await theSession.SaveChangesAsync();

        async Task ForceEventStreamUnexpectedMaxEventIdException()
        {
            await Parallel.ForEachAsync(Enumerable.Range(1, 10), async (_, token) =>
            {
                await using var session = theStore.LightweightSession();
                session.Events.Append(streamId, departed);
                await session.SaveChangesAsync(token);
            });
        }

        var expectedPattern =
            "Unexpected starting version number for event stream '" + streamId +
            "', expected [0-9]{1,2} but was [0-9]{1,2}";
        Should.Throw<EventStreamUnexpectedMaxEventIdException>(ForceEventStreamUnexpectedMaxEventIdException)
            .Message.ShouldMatch(expectedPattern);
    }
}
