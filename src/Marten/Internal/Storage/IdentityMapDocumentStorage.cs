using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LamarCodeGeneration;
using Marten.Exceptions;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Selectors;
using Marten.Schema;
using Npgsql;

namespace Marten.Internal.Storage
{
    public abstract class IdentityMapDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public IdentityMapDocumentStorage(DocumentMapping document) : this(StorageStyle.IdentityMap, document)
        {
        }

        protected IdentityMapDocumentStorage(StorageStyle storageStyle, DocumentMapping document) : base(storageStyle, document)
        {
        }

        public sealed override void Eject(IMartenSession session, T document)
        {
            var id = Identity(document);
            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    d.Remove(id);
                }
            }
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            store(session, document, out var id);
        }

        private void store(IMartenSession session, T document, out TId id)
        {
            id = AssignIdentity(document, session.TenantId, session.Database);
            session.MarkAsAddedForStorage(id, document);

            if (session.ItemMap.TryGetValue(typeof(T), out var items))
            {
                if (items is Dictionary<TId, T> d)
                {
                    if (d.TryGetValue(id, out var existing))
                    {
                        if (!ReferenceEquals(existing, document))
                        {
                            throw new InvalidOperationException(
                                $"Document '{typeof(T).FullNameInCode()}' with same Id already added to the session.");
                        }
                    }
                    else
                    {
                        d[id] = document;
                    }
                }
                else
                {
                    throw new DocumentIdTypeMismatchException(typeof(T), typeof(TId));
                }
            }
            else
            {
                var dict = new Dictionary<TId, T> {{id, document}};
                session.ItemMap.Add(typeof(T), dict);
            }
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            store(session, document, out var id);

            if (version != null)
            {
                session.Versions.StoreVersion<T, TId>(id, version.Value);
            }
            else
            {
                session.Versions.ClearVersion<T, TId>(id);
            }
        }

        public sealed override IReadOnlyList<T> LoadMany(TId[] ids, string tenantId, IMartenSession session)
        {
            var list = preselectLoadedDocuments(ids, tenantId, session, out var command);
            var selector = (ISelector<T>)BuildSelector(session);

            using var reader = session.ExecuteReader(command);
            while (reader.Read())
            {
                var document = selector.Resolve(reader);
                list.Add(document);
            }

            return list;
        }

        private List<T> preselectLoadedDocuments(TId[] ids, string tenantId, IMartenSession session, out NpgsqlCommand command)
        {
            var list = new List<T>();

            Dictionary<TId, T> dict;
            if (session.ItemMap.TryGetValue(typeof(T), out var d))
            {
                dict = (Dictionary<TId, T>) d;
            }
            else
            {
                if (session.ItemMap.TryGetValue(typeof(T), out var d2))
                {
                    dict = (Dictionary<TId, T>) d2;
                }
                else
                {
                    dict = new Dictionary<TId, T>();
                    session.ItemMap.Add(typeof(T), dict);
                }
            }

            var idList = new List<TId>();
            foreach (var id in ids)
            {
                if (dict.TryGetValue(id, out var doc))
                {
                    list.Add(doc);
                }
                else
                {
                    idList.Add(id);
                }
            }

            command = BuildLoadManyCommand(idList.ToArray(), tenantId);
            return list;
        }

        public sealed override async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, string tenantId, IMartenSession session,
            CancellationToken token)
        {
            var list = preselectLoadedDocuments(ids, tenantId, session, out var command);
            var selector = (ISelector<T>)BuildSelector(session);

            await using var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var document = await selector.ResolveAsync(reader, token).ConfigureAwait(false);
                list.Add(document);
            }

            return list;
        }

        public sealed override T Load(TId id, string tenantId, IMartenSession session)
        {
            if (!session.ItemMap.TryGetValue(typeof(T), out var items))
                return load(id, tenantId, session);

            if (items is not Dictionary<TId, T> d)
                throw new DocumentIdTypeMismatchException(typeof(T), typeof(TId));

            if (d.TryGetValue(id, out var item))
                return item;

            return load(id, tenantId, session);
        }

        public sealed override Task<T> LoadAsync(TId id, string tenantId, IMartenSession session, CancellationToken token)
        {
            if (!session.ItemMap.TryGetValue(typeof(T), out var items))
                return loadAsync(id, tenantId, session, token);

            if (items is not Dictionary<TId, T> d)
                throw new DocumentIdTypeMismatchException(typeof(T), typeof(TId));

            if (d.TryGetValue(id, out var item))
                return Task.FromResult(item);

            return loadAsync(id, tenantId, session, token);
        }
    }
}
