using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Resiliency;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Weasel.Core;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon : IProjectionDaemon, IObserver<ShardState>, IDaemonRuntime
{
    private readonly DocumentStore _store;
    private readonly IAgentFactory _factory;
    private ImHashMap<string, ISubscriptionAgent> _agents = ImHashMap<string, ISubscriptionAgent>.Empty;
    private CancellationTokenSource _cancellation = new();
    private readonly HighWaterAgent _highWater;
    private readonly IDisposable _breakSubscription;
    private RetryBlock<DeadLetterEvent> _deadLetterBlock;
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILogger logger, IHighWaterDetector detector,
        IAgentFactory factory)
    {
        Database = database;
        _store = store;
        _factory = factory;
        Logger = logger;
        Tracker = Database.Tracker;
        _highWater = new HighWaterAgent(detector, Tracker, logger, store.Options.Projections, _cancellation.Token);

        _breakSubscription = database.Tracker.Subscribe(this);

        _deadLetterBlock = buildDeadLetterBlock();
    }

    private RetryBlock<DeadLetterEvent> buildDeadLetterBlock()
    {
        return new RetryBlock<DeadLetterEvent>(async (deadLetterEvent, token) =>
        {
            // More important to end cleanly
            if (token.IsCancellationRequested) return;

            await using var session =
                _store.LightweightSession(SessionOptions.ForDatabase(Database));

            await using (session.ConfigureAwait(false))
            {
                session.Store(deadLetterEvent);
                await session.SaveChangesAsync(_cancellation.Token).ConfigureAwait(false);
            }
        }, Logger, _cancellation.Token);
    }

    internal MartenDatabase Database { get; }

    public ILogger Logger { get; }

    public void Dispose()
    {
        _cancellation?.Dispose();
        _highWater?.Dispose();
        _breakSubscription.Dispose();
        _deadLetterBlock.Dispose();
    }

    public ShardStateTracker Tracker { get; }
    public bool IsRunning => _highWater.IsRunning;


    private async Task<bool> tryStartAgentAsync(ISubscriptionAgent agent, ShardExecutionMode mode)
    {
        // Be idempotent, don't start an agent that is already running
        if (_agents.TryFind(agent.Name.Identity, out var running) && running.Status == AgentStatus.Running)
        {
            return false;
        }

        // Lock
        await _semaphore.WaitAsync(_cancellation.Token).ConfigureAwait(false);

        try
        {
            // Be idempotent, don't start an agent that is already running now that we have the lock.
            if (_agents.TryFind(agent.Name.Identity, out running) && running.Status == AgentStatus.Running)
            {
                return false;
            }

            var highWaterMark = HighWaterMark();
            var position = await agent
                .Options
                .DetermineStartingPositionAsync(highWaterMark, agent.Name, mode, Database, _cancellation.Token).ConfigureAwait(false);

            if (position.ShouldUpdateProgressFirst)
            {
                await rewindAgentProgress(agent.Name.Identity, _cancellation.Token, position.Floor).ConfigureAwait(false);
            }

            var errorOptions = mode == ShardExecutionMode.Continuous
                ? _store.Options.Projections.Errors
                : _store.Options.Projections.RebuildErrors;

            await agent.StartAsync(new SubscriptionExecutionRequest(position.Floor, mode, errorOptions, this)).ConfigureAwait(false);
            agent.MarkHighWater(highWaterMark);

            _agents = _agents.AddOrUpdate(agent.Name.Identity, agent);
        }
        finally
        {
            _semaphore.Release();
        }

        return true;
    }

    private async Task rebuildAgent(ISubscriptionAgent agent, long highWaterMark, TimeSpan shardTimeout)
    {
        await _semaphore.WaitAsync(_cancellation.Token).ConfigureAwait(false);

        try
        {
            // Ensure that the agent is stopped if it is already running
            await stopIfRunningAsync(agent.Name.Identity).ConfigureAwait(false);

            var errorOptions = _store.Options.Projections.RebuildErrors;

            var request = new SubscriptionExecutionRequest(0, ShardExecutionMode.Rebuild, errorOptions, this);
            await agent.ReplayAsync(request, highWaterMark, shardTimeout).ConfigureAwait(false);

            _agents = _agents.AddOrUpdate(agent.Name.Identity, agent);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StartAgentAsync(string shardName, CancellationToken token)
    {
        if (!_highWater.IsRunning)
        {
            await StartHighWaterDetectionAsync().ConfigureAwait(false);
        }

        var agent = _factory.BuildProjectionAgentForShard(shardName, Database);
        var didStart = await tryStartAgentAsync(agent, ShardExecutionMode.Continuous).ConfigureAwait(false);

        if (!didStart && agent is IAsyncDisposable d)
        {
            // Could not be started
            await d.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task stopIfRunningAsync(string shardIdentity)
    {
        if (_agents.TryFind(shardIdentity, out var agent))
        {
            var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(5.Seconds());
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, _cancellation.Token);

            try
            {
                await agent.StopAndDrainAsync(linked.Token).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error trying to stop and drain a subscription agent for '{Name}'",
                    agent.Name.Identity);
            }
            finally
            {
                _agents = _agents.Remove(shardIdentity);
            }
        }
    }

    public async Task StopAgentAsync(string shardName, Exception ex = null)
    {
        if (_agents.TryFind(shardName, out var agent))
        {
            await _semaphore.WaitAsync(_cancellation.Token).ConfigureAwait(false);
            try
            {
                var cancellation = new CancellationTokenSource();
                cancellation.CancelAfter(5.Seconds());
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, _cancellation.Token);

                try
                {
                    await agent.StopAndDrainAsync(linked.Token).ConfigureAwait(true);
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Error trying to stop and drain a subscription agent for '{Name}'",
                        agent.Name.Identity);
                }
                finally
                {
                    _agents = _agents.Remove(shardName);

                    if (!_agents.Enumerate().Any() && _highWater.IsRunning)
                    {
                        // Nothing happening, so might as well stop hammering the database!
                        await _highWater.StopAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    public async Task StartAllAsync()
    {
        if (!_highWater.IsRunning)
        {
            await StartHighWaterDetectionAsync().ConfigureAwait(false);
        }

        var agents = _factory.BuildAllProjectionAgents(Database);
        foreach (var agent in agents)
        {
            await tryStartAgentAsync(agent, ShardExecutionMode.Continuous).ConfigureAwait(false);
        }
    }

    public async Task StopAllAsync()
    {
        await _semaphore.WaitAsync(_cancellation.Token).ConfigureAwait(false);

        try
        {
            await _highWater.StopAsync().ConfigureAwait(false);

            var cancellation = new CancellationTokenSource();
            cancellation.CancelAfter(5.Seconds());
            try
            {
                var activeAgents = _agents.Enumerate().Select(x => x.Value).Where(x => x.Status == AgentStatus.Running)
                    .ToArray();
                await Parallel.ForEachAsync(activeAgents, cancellation.Token,
                    (agent, t) => new ValueTask(agent.StopAndDrainAsync(t))).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Nothing, you're already trying to get out
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error trying to stop subscription agents for {Agents}", _agents.Enumerate().Select(x => x.Value.Name.Identity).Join(", "));
            }

            try
            {
                await _deadLetterBlock.DrainAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error trying to finish all outstanding DeadLetterEvent persistence");
            }

            _agents = ImHashMap<string, ISubscriptionAgent>.Empty;

            _deadLetterBlock = buildDeadLetterBlock();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task StartHighWaterDetectionAsync()
    {
        if (_store.Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            await Database.EnsureStorageExistsAsync(typeof(IEvent), _cancellation.Token).ConfigureAwait(false);
        }

        await _highWater.StartAsync().ConfigureAwait(false);
    }

    public Task WaitForNonStaleData(TimeSpan timeout)
    {
        return Database.WaitForNonStaleProjectionDataAsync(timeout);
    }

    public Task WaitForShardToBeRunning(string shardName, TimeSpan timeout)
    {
        if (StatusFor(shardName) == AgentStatus.Running) return Task.CompletedTask;

        Func<ShardState, bool> match = state =>
        {
            if (!state.ShardName.EqualsIgnoreCase(shardName)) return false;

            return state.Action == ShardAction.Started || state.Action == ShardAction.Updated;
        };

        return Tracker.WaitForShardCondition(match, $"Wait for '{shardName}' to be running",timeout);
    }

    public AgentStatus StatusFor(string shardName)
    {
        if (_agents.TryFind(shardName, out var agent))
        {
            return agent.Status;
        }

        return AgentStatus.Stopped;
    }

    public IReadOnlyList<ISubscriptionAgent> CurrentAgents()
    {
        return _agents.Enumerate().Select(x => x.Value).ToList();
    }

    public bool HasAnyPaused()
    {
        return CurrentAgents().Any(x => x.Status == AgentStatus.Paused);
    }

    public void EjectPausedShard(string shardName)
    {
        // Not worried about a lock here.
        _agents = _agents.Remove(shardName);
    }

    public Task PauseHighWaterAgentAsync()
    {
        return _highWater.StopAsync();
    }

    public long HighWaterMark()
    {
        return Tracker.HighWaterMark;
    }

    void IObserver<ShardState>.OnCompleted()
    {
        // Nothing
    }

    void IObserver<ShardState>.OnError(Exception error)
    {
        // Nothing
    }

    void IObserver<ShardState>.OnNext(ShardState value)
    {
        if (value.ShardName == ShardState.HighWaterMark)
        {
            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug("Event high water mark detected at {Sequence}", value.Sequence);
            }

            foreach (var agent in CurrentAgents())
            {
                agent.MarkHighWater(value.Sequence);
            }
        }
    }

    public Task RecordDeadLetterEventAsync(DeadLetterEvent @event)
    {
        return _deadLetterBlock.PostAsync(@event);
    }



}
