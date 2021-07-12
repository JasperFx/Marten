using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Projections.Async;
using Marten.Events.Projections.Async.ErrorHandling;
using Marten.Storage;
using Marten.Testing.Harness;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Marten.Util;
using Shouldly;

namespace Marten.Testing.Bugs
{
    [Collection("Bug1845")]
    public class Bug_1845_async_daemon_skips_events_on_stopall: OneOffConfigurationsContext
    {
        private readonly IDaemonLogger _logger;

        public Bug_1845_async_daemon_skips_events_on_stopall(ITestOutputHelper output) : base("Bug1845")
        {
            _logger = new TracingLogger(output.WriteLine);
        }

        [Fact]
        public async Task error_in_projection_with_stop_strategy_should_drop_pending_events()
        {

            StoreOptions(_ =>
            {
                _.Events.DatabaseSchemaName = "Bug1845";
                _.Events.StreamIdentity = StreamIdentity.AsString;
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            PublishEvents();

            var settings = new DaemonSettings
            {
                FetchingCooldown = TimeSpan.FromMilliseconds(500),
                LeadingEdgeBuffer = TimeSpan.FromMilliseconds(100)
            };

            const int retryCount = 2;

            settings.ExceptionHandling
                .OnException<Exception>()
                .Retry(retryCount, TimeSpan.FromSeconds(2))
                .AfterMaxAttempts = new StopAll(x =>
                {
                    _logger.Error(x);
                });

            var projection = new ErroringProjection(theStore, retryCount);
            IProjection[] projections = { projection };

            using (var daemon = theStore.BuildProjectionDaemon(logger: _logger, settings: settings, projections: projections))
            {
                daemon.StartAll();
                projection.task.Wait(TimeSpan.FromSeconds(10));
                Thread.Sleep(TimeSpan.FromSeconds(10));
            }

            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var command = conn.Connection.CreateCommand();

                command.Sql($"select last_seq_id from {theStore.Events.DatabaseSchemaName}.mt_event_progression where name = :name")
                    .With("name", typeof(ErroringProjection).FullName);

                using (var reader = await command.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    var any = await reader.ReadAsync().ConfigureAwait(false);
                    if (!any)
                    {
                        throw new Exception("No projection found");
                    }

                    var lastEncountered = await reader.GetFieldValueAsync<long>(0);
                    lastEncountered.ShouldBe(10);
                }
            }
        }

        public void PublishEvents()
        {
            var listOfEvents = new List<object>();

            for (var i = 0; i < 15; i++)
            {
                listOfEvents.Add(new SomethingHappened { Id = i });
            }

            listOfEvents.Add(new FailureIntroduced { Id = 15 });

            using (var session = theStore.OpenSession())
            {
                session.Events.Append("TestStream001", listOfEvents);
                session.SaveChanges();
            }

        }
    }

    public class ErroringProjection: IProjection
    {
        private readonly DocumentStore store;
        private int retryCount;
        private readonly TaskCompletionSource<bool> completionSource;

        public ErroringProjection(DocumentStore store, int retryCount)
        {
            this.store = store;
            this.retryCount = retryCount;
            completionSource = new TaskCompletionSource<bool>();
        }

        public Type[] Consumes { get; } = new Type[] { typeof(SomethingHappened), typeof(FailureIntroduced), typeof(Failed) };

        public AsyncOptions AsyncOptions { get; } = new AsyncOptions { PageSize = 10 };

        public void Apply(IDocumentSession session, EventPage page)
        {
            throw new NotImplementedException();
        }

        public Task ApplyAsync(IDocumentSession session, EventPage page, CancellationToken token)
        {
            foreach (var evt in page.Events)
            {
                if (evt.Data.GetType() == typeof(FailureIntroduced))
                {
                    using (var newSession = store.OpenSession())
                    {
                        newSession.Events.Append("DeadMessageStream001", new Failed { Id = 100 });
                        newSession.SaveChanges();
                    }
                    retryCount--;
                    if (retryCount <= 0)
                        completionSource.SetResult(true);

                    throw new Exception();
                }
            }

            return Task.CompletedTask;
        }

        public Task task => completionSource.Task;

        public void EnsureStorageExists(ITenant tenant)
        {
        }
    }

    public class SomethingHappened
    {
        public int Id { get; set; }
    }
    public class FailureIntroduced
    {
        public int Id { get; set; }
    }
    public class Failed
    {
        public int Id { get; set; }
    }
}
