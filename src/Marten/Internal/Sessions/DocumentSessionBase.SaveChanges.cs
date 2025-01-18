#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Npgsql;
using Weasel.Core;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase
{
    public void SaveChanges()
    {
        assertNotDisposed();

        processChangeTrackers();
        if (!_workTracker.HasOutstandingWork())
        {
            return;
        }

        foreach (var listener in Listeners) listener.BeforeProcessChanges(this);

        try
        {
            Options.EventGraph.ProcessEvents(this);
        }
        catch (Exception)
        {
            tryApplyTombstoneBatch();
            throw;
        }

        _workTracker.Sort(Options);

        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            foreach (var operationType in operationDocumentTypes()) Database.EnsureStorageExists(operationType);
        }

        foreach (var listener in Listeners)
        {
            listener.BeforeSaveChanges(this);
        }

        var batch = new UpdateBatch(_workTracker.AllOperations);

        ExecuteBatch(batch);

        resetDirtyChecking();

        EjectPatchedTypes(_workTracker);
        Logger.RecordSavedChanges(this, _workTracker);

        foreach (var listener in Listeners)
        {
            listener.AfterCommit(this, _workTracker);
        }

        // Need to clear the unit of work here
        _workTracker.Reset();
    }

    public async Task SaveChangesAsync(CancellationToken token = default)
    {
        assertNotDisposed();

        processChangeTrackers();
        if (!_workTracker.HasOutstandingWork())
        {
            return;
        }

        foreach (var listener in Listeners)
        {
            await listener.BeforeProcessChangesAsync(this, token).ConfigureAwait(false);
        }

        try
        {
            await Options.EventGraph.ProcessEventsAsync(this, token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await tryApplyTombstoneEventsAsync(token).ConfigureAwait(false);

            throw;
        }

        _workTracker.Sort(Options);

        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            foreach (var operationType in operationDocumentTypes())
            {
                await Database.EnsureStorageExistsAsync(operationType, token).ConfigureAwait(false);
            }
        }

        foreach (var listener in Listeners)
        {
            await listener.BeforeSaveChangesAsync(this, token).ConfigureAwait(false);
        }

        var batch = new UpdateBatch(_workTracker.AllOperations);

        await ExecuteBatchAsync(batch, token).ConfigureAwait(false);

        resetDirtyChecking();

        EjectPatchedTypes(_workTracker);
        Logger.RecordSavedChanges(this, _workTracker);

        foreach (var listener in Listeners)
        {
            await listener.AfterCommitAsync(this, _workTracker, token).ConfigureAwait(false);
        }

        // Need to clear the unit of work here
        _workTracker.Reset();
    }

    private IEnumerable<Type> operationDocumentTypes()
    {
        return _workTracker.Operations().Select(x => x.DocumentType).Where(x => x != null).Distinct();
    }

    internal void ExecuteBatch(IUpdateBatch batch)
    {
        var pages = batch.BuildPages(this);

        var execution = new PagesExecution(pages, _connection);

        try
        {
            try
            {
                Options.ResiliencePipeline.Execute(static (x, t) => x.Connection.ExecuteBatchPages(x.Pages, x.Exceptions), execution, CancellationToken.None);
            }
            catch (Exception e)
            {
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (execution.Exceptions.Count == 1)
            {
                var ex = execution.Exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (execution.Exceptions.Any())
            {
                throw new AggregateException(execution.Exceptions);
            }
        }
        catch (Exception)
        {
            tryApplyTombstoneBatch();

            throw;
        }
    }

    private void tryApplyTombstoneBatch()
    {
        if (Options.EventGraph.TryCreateTombstoneBatch(this, out var tombstoneBatch))
        {
            Options.EventGraph.PostTombstones(tombstoneBatch);
        }
    }

    internal record PagesExecution(IReadOnlyList<OperationPage> Pages, IConnectionLifetime Connection)
    {
        public List<Exception> Exceptions { get; } = new();
    }

    internal async Task ExecuteBatchAsync(IUpdateBatch batch, CancellationToken token)
    {
        // TODO -- double check this isn't getting done multiple times
        if (Options.AutoCreateSchemaObjects != AutoCreate.None)
        {
            foreach (var documentType in batch.DocumentTypes())
            {
                await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
            }
        }

        var pages = batch.BuildPages(this);
        if (!pages.Any()) return;

        var execution = new PagesExecution(pages, _connection);

        try
        {
            try
            {
                await executeBeforeCommitListeners(batch).ConfigureAwait(false);

                await Options.ResiliencePipeline.ExecuteAsync(
                    static (e, t) => new ValueTask(e.Connection.ExecuteBatchPagesAsync(e.Pages, e.Exceptions, t)), execution, token).ConfigureAwait(false);

                await executeAfterCommitListeners(batch).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>().Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (execution.Exceptions.Count == 1)
            {
                var ex = execution.Exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (execution.Exceptions.Any())
            {
                throw new AggregateException(execution.Exceptions);
            }
        }
        catch (Exception)
        {
            await tryApplyTombstoneEventsAsync(token).ConfigureAwait(false);
            throw;
        }
    }

    private async Task executeAfterCommitListeners(IUpdateBatch batch)
    {
        try
        {
            await batch.PostUpdateAsync(this).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogFailure(new NpgsqlCommand(), e);
        }
    }

    private async Task executeBeforeCommitListeners(IUpdateBatch batch)
    {
        try
        {
            await batch.PreUpdateAsync(this).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.LogFailure(new NpgsqlCommand(), e);
        }
    }

    protected virtual Task tryApplyTombstoneEventsAsync(CancellationToken token)
    {
        if (Options.EventGraph.TryCreateTombstoneBatch(this, out var tombstoneBatch))
        {
            return Options.EventGraph.PostTombstonesAsync(tombstoneBatch);
        }

        return Task.CompletedTask;
    }
}
