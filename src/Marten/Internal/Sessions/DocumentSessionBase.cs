#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Resiliency;
using Marten.Exceptions;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.Metadata;
using Marten.Services;
using Marten.Storage;

namespace Marten.Internal.Sessions;

public abstract partial class DocumentSessionBase: QuerySession, IDocumentSession
{
    internal readonly ISessionWorkTracker _workTracker;

    private Dictionary<string, NestedTenantSession>? _byTenant;

    internal DocumentSessionBase(
        DocumentStore store,
        SessionOptions sessionOptions,
        IConnectionLifetime connection
    ): base(store, sessionOptions, connection)
    {
        Concurrency = sessionOptions.ConcurrencyChecks;
        _workTracker = new UnitOfWork(this);
    }

    internal DocumentSessionBase(
        DocumentStore store,
        SessionOptions sessionOptions,
        IConnectionLifetime connection,
        ISessionWorkTracker workTracker,
        Tenant? tenant = default
    ): base(store, sessionOptions, connection, tenant)
    {
        Concurrency = sessionOptions.ConcurrencyChecks;
        _workTracker = workTracker;
    }

    internal ValueTask<IMessageBatch> CurrentMessageBatch()
    {
        if (_workTracker is ProjectionUpdateBatch batch)
        {
            return batch.CurrentMessageBatch(this);
        }

        throw new InvalidOperationException("This session is not a ProjectionDocumentSession");
    }

    internal ITenancy Tenancy => DocumentStore.As<DocumentStore>().Tenancy;

    internal ISessionWorkTracker WorkTracker => _workTracker;

    public void EjectAllPendingChanges()
    {
        _workTracker.EjectAll();
        ChangeTrackers.Clear();
    }


    public void Store<T>(IEnumerable<T> entities) where T : notnull
    {
        Store(entities?.ToArray()!);
    }

    public void Store<T>(params T[] entities) where T : notnull
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (typeof(T).IsGenericEnumerable())
        {
            throw new ArgumentOutOfRangeException(typeof(T).Name,
                "Do not use IEnumerable<T> here as the document type. Either cast entities to an array instead or use the IEnumerable<T> Store() overload instead.");
        }

        store(entities);
    }

    public void UpdateExpectedVersion<T>(T entity, Guid version) where T : notnull
    {
        assertNotDisposed();

        var storage = StorageFor<T>();
        storage.Store(this, entity, version);
        var op = storage.Upsert(entity, this, TenantId);
        _workTracker.Add(op);
    }

    public void UpdateRevision<T>(T entity, int revision) where T : notnull
    {
        assertNotDisposed();

        var storage = StorageFor<T>();
        storage.Store(this, entity, revision);
        var op = storage.Upsert(entity, this, TenantId);
        if (op is IRevisionedOperation r)
        {
            r.Revision = revision;
        }
        _workTracker.Add(op);
    }

    public void TryUpdateRevision<T>(T entity, int revision)
    {
        assertNotDisposed();

        var storage = StorageFor<T>();
        storage.Store(this, entity, revision);
        var op = storage.Upsert(entity, this, TenantId);
        if (op is IRevisionedOperation r)
        {
            r.Revision = revision;
            r.IgnoreConcurrencyViolation = true;
        }
        _workTracker.Add(op);
    }

    public void Insert<T>(IEnumerable<T> entities) where T : notnull
    {
        Insert(entities.ToArray());
    }

    public void Insert<T>(params T[] entities) where T : notnull
    {
        assertNotDisposed();

        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (typeof(T).IsGenericEnumerable())
        {
            throw new ArgumentOutOfRangeException(typeof(T).Name,
                "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
        }

        if (typeof(T) == typeof(object))
        {
            InsertObjects(entities.OfType<object>());
        }
        else
        {
            var storage = StorageFor<T>();

            foreach (var entity in entities)
            {
                storage.Store(this, entity);
                var op = storage.Insert(entity, this, TenantId);
                if (op is IRevisionedOperation r) r.Revision = 1;
                _workTracker.Add(op);
            }
        }
    }

    public void Update<T>(IEnumerable<T> entities) where T : notnull
    {
        Update(entities.ToArray());
    }

    public void Update<T>(params T[] entities) where T : notnull
    {
        assertNotDisposed();

        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (typeof(T).IsGenericEnumerable())
        {
            throw new ArgumentOutOfRangeException(typeof(T).Name,
                "Do not use IEnumerable<T> here as the document type. You may need to cast entities to an array instead.");
        }

        if (typeof(T) == typeof(object))
        {
            InsertObjects(entities.OfType<object>());
        }
        else
        {
            var storage = StorageFor<T>();

            if (Concurrency == ConcurrencyChecks.Disabled && storage.UseOptimisticConcurrency)
            {
                foreach (var entity in entities)
                {
                    storage.Store(this, entity);
                    var op = storage.Update(entity, this, TenantId);
                    _workTracker.Add(op);
                }
            }
            else
            {
                foreach (var entity in entities)
                {
                    storeEntity(entity, storage);
                    var op = storage.Update(entity, this, TenantId);
                    _workTracker.Add(op);
                }
            }
        }
    }

    public void InsertObjects(IEnumerable<object> documents)
    {
        assertNotDisposed();

        documents.Where(x => x != null).GroupBy(x => x.GetType()).Each(group =>
        {
            var handler = typeof(InsertHandler<>).CloseAndBuildAs<IObjectHandler>(group.Key);
            handler.Execute(this, group);
        });
    }

    public void QueueSqlCommand(string sql, params object[] parameterValues)
    {
        QueueSqlCommand(DefaultParameterPlaceholder, sql, parameterValues: parameterValues);
    }

    public void QueueSqlCommand(char placeholder, string sql, params object[] parameterValues)
    {
        sql = sql.TrimEnd(';');
        if (sql.Contains(';'))
            throw new ArgumentOutOfRangeException(nameof(sql),
                "You must specify one SQL command at a time because of Marten's usage of command batching. ';' cannot be used as a command separator here.");

        var operation = new ExecuteSqlStorageOperation(placeholder, sql, parameterValues);
        QueueOperation(operation);
    }

    public IUnitOfWork PendingChanges => _workTracker;

    public void StoreObjects(IEnumerable<object> documents)
    {
        assertNotDisposed();

        var documentsGroupedByType = documents
            .Where(x => x != null)
            .GroupBy(x => x.GetType());

        foreach (var group in documentsGroupedByType)
        {
            // Build the right handler for the group type
            var handler = typeof(StoreHandler<>).CloseAndBuildAs<IObjectHandler>(group.Key);
            handler.Execute(this, group);
        }
    }

    public new IEventStore Events => (IEventStore)base.Events;


    public void QueueOperation(IStorageOperation storageOperation)
    {
        _workTracker.Add(storageOperation);
    }

    public virtual void Eject<T>(T document) where T : notnull
    {
        StorageFor<T>().Eject(this, document);
        _workTracker.Eject(document);

        ChangeTrackers.RemoveAll(x => ReferenceEquals(document, x.Document));
    }

    public virtual void EjectAllOfType(Type type)
    {
        ItemMap.Remove(type);

        _workTracker.EjectAllOfType(type);

        ChangeTrackers.RemoveAll(x => x.Document.GetType().CanBeCastTo(type));
    }

    public void SetHeader(string key, object value)
    {
        Headers ??= new Dictionary<string, object>();

        Headers[key] = value;
    }

    public object? GetHeader(string key)
    {
        return Headers?.TryGetValue(key, out var value) ?? false ? value : null;
    }

    /// <summary>
    ///     Access data from another tenant and apply document or event updates to this
    ///     IDocumentSession for a separate tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    public new ITenantOperations ForTenant(string tenantId)
    {
        _byTenant ??= new Dictionary<string, NestedTenantSession>();

        if (_byTenant.TryGetValue(tenantId, out var tenantSession))
        {
            return tenantSession;
        }

        var isValid = DocumentStore.Options.Tenancy.IsTenantStoredInCurrentDatabase(Database, tenantId);
        if (!isValid)
        {
            throw new InvalidTenantForDatabaseException(tenantId, Database);
        }

        var tenant = new Tenant(tenantId, Database);
        tenantSession = new NestedTenantSession(this, tenant);
        _byTenant[tenantId] = tenantSession;

        return tenantSession;
    }

    protected override IQueryEventStore CreateEventStore(DocumentStore store, Tenant tenant)
    {
        return new EventStore(this, store, tenant);
    }

    protected internal abstract void ejectById<T>(long id) where T : notnull;
    protected internal abstract void ejectById<T>(int id) where T : notnull;
    protected internal abstract void ejectById<T>(Guid id) where T : notnull;
    protected internal abstract void ejectById<T>(string id) where T : notnull;

    protected internal virtual void processChangeTrackers()
    {
        // Nothing
    }

    protected internal virtual void resetDirtyChecking()
    {
        // Nothing
    }

    private void store<T>(IEnumerable<T> entities) where T : notnull
    {
        assertNotDisposed();

        if (typeof(T) == typeof(object))
        {
            StoreObjects(entities.OfType<object>());
        }
        else
        {
            var storage = StorageFor<T>();

            if (Concurrency == ConcurrencyChecks.Disabled && (storage.UseOptimisticConcurrency || storage.UseNumericRevisions))
            {
                foreach (var entity in entities)
                {
                    // Put it in the identity map -- if necessary
                    storage.Store(this, entity);

                    var overwrite = storage.Overwrite(entity, this, TenantId);

                    _workTracker.Add(overwrite);
                }
            }
            else
            {
                foreach (var entity in entities)
                {
                    storeEntity(entity, storage);

                    var upsert = storage.Upsert(entity, this, TenantId);

                    _workTracker.Add(upsert);
                }
            }
        }
    }

    private void storeEntity<T>(T entity, IDocumentStorage<T> storage) where T : notnull
    {
        switch (entity)
        {
            case IVersioned versioned when versioned.Version != Guid.Empty:
                storage.Store(this, entity, versioned.Version);
                return;
            case IRevisioned revisioned when revisioned.Version != 0:
                storage.Store(this, entity, revisioned.Version);
                return;
            default:
                // Put it in the identity map -- if necessary
                storage.Store(this, entity);
                break;
        }
    }

    public void EjectPatchedTypes(IUnitOfWork changes)
    {
        var patchedTypes = changes.Operations().Where(x => x.Role() == OperationRole.Patch).Select(x => x.DocumentType)
            .Distinct().ToArray();
        foreach (var type in patchedTypes) EjectAllOfType(type);
    }

    internal interface IObjectHandler
    {
        void Execute(IDocumentSession session, IEnumerable<object> objects);
    }

    internal class StoreHandler<T>: IObjectHandler where T : notnull
    {
        public void Execute(IDocumentSession session, IEnumerable<object> objects)
        {
            // Delegate to the Store<T>() method
            session.Store(objects.OfType<T>().ToArray());
        }
    }

    internal class InsertHandler<T>: IObjectHandler where T : notnull
    {
        public void Execute(IDocumentSession session, IEnumerable<object> objects)
        {
            session.Insert(objects.OfType<T>().ToArray());
        }
    }

    internal class DeleteHandler<T>: IObjectHandler where T : notnull
    {
        public void Execute(IDocumentSession session, IEnumerable<object> objects)
        {
            foreach (var document in objects.OfType<T>()) session.Delete(document);
        }
    }

    internal void StoreDocumentInItemMap<TDoc, TId>(TId id, TDoc document) where TDoc : class
    {
        if (ItemMap.ContainsKey(typeof(TDoc)))
        {
            ItemMap[typeof(TDoc)].As<Dictionary<TId, TDoc>>()[id] = document;
        }
        else
        {
            var dict = new Dictionary<TId, TDoc>();
            dict[id] = document;
            ItemMap[typeof(TDoc)] = dict;
        }
    }

    internal bool TryGetAggregateFromIdentityMap<TDoc, TId>(TId id, out TDoc document)
    {
        if (Options.EventGraph.UseIdentityMapForAggregates)
        {
            if (ItemMap.TryGetValue(typeof(TDoc), out var raw))
            {
                if (raw is Dictionary<TId, TDoc> dict)
                {
                    if (dict.TryGetValue(id, out var doc))
                    {
                        document = doc;
                        return true;
                    }
                }
            }
        }

        document = default;
        return false;
    }
}
