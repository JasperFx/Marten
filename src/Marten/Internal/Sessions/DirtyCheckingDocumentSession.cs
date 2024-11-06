#nullable enable
using System;
using System.Linq;
using JasperFx.Core;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Services;

namespace Marten.Internal.Sessions;

public class DirtyCheckingDocumentSession: DocumentSessionBase
{
    internal DirtyCheckingDocumentSession(DocumentStore store, SessionOptions sessionOptions,
        IConnectionLifetime connection): base(store, sessionOptions, connection)
    {
    }

    internal override DocumentTracking TrackingMode => DocumentTracking.DirtyTracking;

    protected internal override IDocumentStorage<T> selectStorage<T>(DocumentProvider<T> provider)
    {
        return provider.DirtyTracking;
    }

    protected internal override void processChangeTrackers()
    {
        foreach (var tracker in ChangeTrackers)
        {
            if (tracker.DetectChanges(this, out var operation))
            {
                _workTracker.Add(operation);
            }
        }
    }

    protected internal override void resetDirtyChecking()
    {
        foreach (var tracker in ChangeTrackers) tracker.Reset(this);

        var knownDocuments = ChangeTrackers.Select(x => x.Document).ToArray();

        var operations = _workTracker.AllOperations
            .OfType<IDocumentStorageOperation>()
            .Where(x => !knownDocuments.Contains(x.Document));

        foreach (var operation in operations)
        {
            var tracker = operation.ToTracker(this);
            ChangeTrackers.Add(tracker);
        }
    }

    protected internal override void ejectById<T>(long id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }

    protected internal override void ejectById<T>(int id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }

    protected internal override void ejectById<T>(Guid id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }

    protected internal override void ejectById<T>(string id)
    {
        var documentStorage = StorageFor<T>();
        documentStorage.EjectById(this, id);
        documentStorage.RemoveDirtyTracker(this, id);
    }
}
