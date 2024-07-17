using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Internal.Operations;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal;

internal class UnitOfWork: ISessionWorkTracker
{
    private readonly List<IStorageOperation> _eventOperations = new();
    private readonly List<IStorageOperation> _operations = new();
    private readonly IMartenSession _parent;

    public UnitOfWork(IMartenSession parent)
    {
        _parent = parent;
    }

    internal UnitOfWork(IEnumerable<IStorageOperation> operations)
    {
        _operations.AddRange(operations);
    }

    public void Reset()
    {
        _operations.Clear();
        _eventOperations.Clear();
        Streams.Clear();
    }

    public void Add(IStorageOperation operation)
    {
        if (operation is IDocumentStorageOperation o)
        {
            _operations.RemoveAll(x =>
                x is IDocumentStorageOperation && x.As<IDocumentStorageOperation>().Document == o.Document);
        }

        if (operation.DocumentType == typeof(IEvent))
        {
            _eventOperations.Add(operation);
        }
        else
        {
            _operations.Add(operation);
        }
    }

    public IReadOnlyList<IStorageOperation> AllOperations => _eventOperations.Concat(_operations).ToList();

    public void Sort(StoreOptions options)
    {
        if (shouldSort(options, out var comparer))
        {
            var sorted = _operations.OrderBy(f => f, comparer).ToList();
            _operations.Clear();
            _operations.AddRange(sorted);
        }
    }


    IEnumerable<IDeletion> IUnitOfWork.Deletions()
    {
        return _operations.OfType<IDeletion>();
    }

    IEnumerable<IDeletion> IUnitOfWork.DeletionsFor<T>()
    {
        return _operations.OfType<IDeletion>().Where(x => x.DocumentType.CanBeCastTo<T>());
    }

    IEnumerable<IDeletion> IUnitOfWork.DeletionsFor(Type documentType)
    {
        return _operations.OfType<IDeletion>().Where(x => x.DocumentType.CanBeCastTo(documentType));
    }

    IEnumerable<object> IUnitOfWork.Updates()
    {
        var fromTrackers = _parent.ChangeTrackers
            .Where(x => x.DetectChanges(_parent, out var _)).Select(x => x.Document);

        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Update || x.Role() == OperationRole.Upsert)
            .Select(x => x.Document).Union(fromTrackers);
    }

    IEnumerable<object> IUnitOfWork.Inserts()
    {
        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Insert)
            .Select(x => x.Document);
    }

    IEnumerable<T> IUnitOfWork.UpdatesFor<T>()
    {
        var fromTrackers = _parent.ChangeTrackers
            .Where(x => x.Document.GetType().CanBeCastTo<T>())
            .Where(x => x.DetectChanges(_parent, out var _))
            .Select(x => x.Document).OfType<T>();

        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Update || x.Role() == OperationRole.Upsert)
            .Select(x => x.Document)
            .OfType<T>()
            .Concat(fromTrackers)
            .Distinct();
    }

    IEnumerable<T> IUnitOfWork.InsertsFor<T>()
    {
        return _operations
            .OfType<IDocumentStorageOperation>()
            .Where(x => x.Role() == OperationRole.Insert)
            .Select(x => x.Document)
            .OfType<T>();
    }

    IEnumerable<T> IUnitOfWork.AllChangedFor<T>()
    {
        var fromTrackers = _parent.ChangeTrackers
            .Where(x => x.Document.GetType().CanBeCastTo<T>())
            .Where(x => x.DetectChanges(_parent, out var _))
            .Select(x => x.Document).OfType<T>();


        return _operations
            .OfType<IDocumentStorageOperation>()
            .Select(x => x.Document)
            .OfType<T>()
            .Concat(fromTrackers)
            .Distinct();
    }

    public List<StreamAction> Streams { get; } = new();

    IList<StreamAction> IUnitOfWork.Streams()
    {
        return Streams;
    }

    IEnumerable<IStorageOperation> IUnitOfWork.Operations()
    {
        return _operations;
    }

    IEnumerable<IStorageOperation> IUnitOfWork.OperationsFor<T>()
    {
        return _operations.Where(x => x.DocumentType.CanBeCastTo<T>());
    }

    IEnumerable<IStorageOperation> IUnitOfWork.OperationsFor(Type documentType)
    {
        return _operations.Where(x => x.DocumentType.CanBeCastTo(documentType));
    }

    IEnumerable<object> IChangeSet.Updated => _operations.OfType<IDocumentStorageOperation>()
        .Where(x => x.Role() == OperationRole.Update || x.Role() == OperationRole.Upsert).Select(x => x.Document);

    IEnumerable<object> IChangeSet.Inserted => _operations.OfType<IDocumentStorageOperation>()
        .Where(x => x.Role() == OperationRole.Insert).Select(x => x.Document);

    IEnumerable<IDeletion> IChangeSet.Deleted => _operations.OfType<IDeletion>();

    IEnumerable<IEvent> IChangeSet.GetEvents()
    {
        return Streams.SelectMany(x => x.Events);
    }

    IEnumerable<StreamAction> IChangeSet.GetStreams()
    {
        return Streams;
    }

    public IChangeSet Clone()
    {
        var clone = new UnitOfWork(_operations);
        clone.Streams.AddRange(Streams);

        return clone;
    }

    public void Eject<T>(T document)
    {
        var operations = operationsFor(typeof(T));
        var matching = operations.OfType<IDocumentStorageOperation>().Where(x => ReferenceEquals(document, x.Document))
            .ToArray();

        foreach (var operation in matching) _operations.Remove(operation);
    }

    public void EjectAllOfType(Type type)
    {
        var operations = operationsFor(type);
        var matching = operations.OfType<IDocumentStorageOperation>().ToArray();

        foreach (var operation in matching) _operations.Remove(operation);
    }

    public bool HasOutstandingWork()
    {
        return _operations.Any() || Streams.Any(x => x.Events.Count > 0) || _eventOperations.Any();
    }

    public void EjectAll()
    {
        _operations.Clear();
        _eventOperations.Clear();
        Streams.Clear();
    }

    public void PurgeOperations<T, TId>(TId id) where T : notnull
    {
        _operations.RemoveAll(op => op is StorageOperation<T, TId> storage && storage.Id.Equals(id));
    }

    public bool TryFindStream(string streamKey, out StreamAction stream)
    {
        stream = Streams
            .FirstOrDefault(x => x.Key == streamKey);

        return stream != null;
    }

    public bool TryFindStream(Guid streamId, out StreamAction stream)
    {
        stream = Streams
            .FirstOrDefault(x => x.Id == streamId);

        return stream != null;
    }

    private bool shouldSort(StoreOptions options, out IComparer<IStorageOperation> comparer)
    {
        comparer = null;
        if (_operations.Count <= 1)
        {
            return false;
        }

        var rawTypes = _operations
            .Where(x => x.Role() != OperationRole.Other)
            .Select(x => x.DocumentType)
            .Where(x => x != null)
            .Where(x => x != typeof(StorageFeatures))
            .Distinct().ToArray();

        if (rawTypes.Length <= 1) return false;

        var hasRelationship = rawTypes.Any(x => options.Storage.GetTypeDependencies(x).Intersect(rawTypes).Any());

        if (!hasRelationship) return false;

        var types = rawTypes
            .TopologicalSort(type => options.Storage.GetTypeDependencies(type)).ToArray();

        if (_operations.OfType<IDeletion>().Any())
        {
            comparer = new StorageOperationWithDeletionsComparer(types);
        }
        else
        {
            comparer = new StorageOperationByTypeComparer(types);
        }

        return true;
    }

    private IEnumerable<IStorageOperation> operationsFor(Type documentType)
    {
        return _operations.Where(x => x.DocumentType == documentType);
    }

    private class StorageOperationWithDeletionsComparer: IComparer<IStorageOperation>
    {
        private readonly Type[] _topologicallyOrderedTypes;

        public StorageOperationWithDeletionsComparer(Type[] topologicallyOrderedTypes)
        {
            _topologicallyOrderedTypes = topologicallyOrderedTypes;
        }

        public int Compare(IStorageOperation x, IStorageOperation y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x?.DocumentType == null || y?.DocumentType == null)
            {
                return 0;
            }

            // Maintain order if same document type and same operation
            if (x.DocumentType == y.DocumentType && x.GetType() == y.GetType())
            {
                return 0;
            }

            var xIndex = FindIndex(x);
            var yIndex = FindIndex(y);

            var xIsDelete = x is IDeletion;
            var yIsDelete = y is IDeletion;

            if (xIsDelete != yIsDelete)
            {
                // Arbitrary order if one is a delete but the other is not, because this will force the sorting
                // to try and compare these documents against others and fall in to the below checks.
                return yIsDelete ? 1 : -1;
            }

            if (xIsDelete)
            {
                // Both are deletes, so we need reverse topological order to inserts, updates and upserts
                return yIndex.CompareTo(xIndex);
            }

            // Both are inserts, updates or upserts so topological
            return xIndex.CompareTo(yIndex);
        }

        private int FindIndex(IStorageOperation x)
        {
            // Will loop through up the inheritance chain until reaches the end or the index is found, used
            // to handle inheritance as topologically sorted array may not have the subclasses listed
            var documentType = x.DocumentType;
            var index = 0;

            do
            {
                index = _topologicallyOrderedTypes.IndexOf(documentType);
                documentType = documentType.BaseType;
            } while (index == -1 && documentType != null);

            return index;
        }
    }

    private class StorageOperationByTypeComparer: IComparer<IStorageOperation>
    {
        private readonly Type[] _topologicallyOrderedTypes;

        public StorageOperationByTypeComparer(Type[] topologicallyOrderedTypes)
        {
            _topologicallyOrderedTypes = topologicallyOrderedTypes;
        }

        public int Compare(IStorageOperation x, IStorageOperation y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x?.DocumentType == null || y?.DocumentType == null)
            {
                return 0;
            }

            if (x.DocumentType == y.DocumentType)
            {
                return 0;
            }

            var xIndex = FindIndex(x);
            var yIndex = FindIndex(y);

            return xIndex.CompareTo(yIndex);
        }

        private int FindIndex(IStorageOperation x)
        {
            // Will loop through up the inheritance chain until reaches the end or the index is found, used
            // to handle inheritance as topologically sorted array may not have the subclasses listed
            var documentType = x.DocumentType;
            var index = 0;

            do
            {
                index = _topologicallyOrderedTypes.IndexOf(documentType);
                documentType = documentType.BaseType;
            } while (index == -1 && documentType != null);

            return index;
        }
    }
}
