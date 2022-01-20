using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Selectors;
using Marten.Schema;

namespace Marten.Internal.Storage
{
    public abstract class LightweightDocumentStorage<T, TId>: DocumentStorage<T, TId>
    {
        public LightweightDocumentStorage(DocumentMapping document) : base(StorageStyle.Lightweight, document)
        {
        }

        public sealed override void Store(IMartenSession session, T document)
        {
            var id = AssignIdentity(document, session.TenantId, session.Database);
            session.MarkAsAddedForStorage(id, document);
        }

        public sealed override void Store(IMartenSession session, T document, Guid? version)
        {
            var id = AssignIdentity(document, session.TenantId, session.Database);
            session.MarkAsAddedForStorage(id, document);

            if (version.HasValue)
            {
                session.Versions.StoreVersion<T, TId>(id, version.Value);
            }
            else
            {
                session.Versions.ClearVersion<T, TId>(id);
            }


        }

        public sealed override void Eject(IMartenSession session, T document)
        {
            // Nothing!
        }

        public sealed override IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session)
        {
            var list = new List<T>();

            var command = BuildLoadManyCommand(ids, session.TenantId);
            var selector = (ISelector<T>)BuildSelector(session);

            using (var reader = session.ExecuteReader(command))
            {
                while (reader.Read())
                {
                    var document = selector.Resolve(reader);
                    list.Add(document);
                }
            }

            return list;
        }

        public sealed override async Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session,
            CancellationToken token)
        {
            var list = new List<T>();

            var command = BuildLoadManyCommand(ids, session.TenantId);
            var selector = (ISelector<T>)BuildSelector(session);

            using (var reader = await session.ExecuteReaderAsync(command, token).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                {
                    var document = await selector.ResolveAsync(reader, token).ConfigureAwait(false);
                    list.Add(document);
                }
            }

            return list;
        }

        public sealed override T Load(TId id, IMartenSession session)
        {
            return load(id, session);
        }


        public sealed override Task<T> LoadAsync(TId id, IMartenSession session, CancellationToken token)
        {
            return loadAsync(id, session, token);
        }
    }
}
