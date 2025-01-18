using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema.BulkLoading;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.CodeGeneration;

public class SubClassBulkLoader<T, TRoot>: IBulkLoader<T> where T : TRoot
{
    private readonly IBulkLoader<TRoot> _inner;

    public SubClassBulkLoader(IBulkLoader<TRoot> inner)
    {
        _inner = inner;
    }

    public void Load(Tenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
    {
        _inner.Load(tenant, serializer, conn, documents.OfType<TRoot>());
    }

    public Task LoadAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
        CancellationToken cancellation)
    {
        return _inner.LoadAsync(tenant, serializer, conn, documents.OfType<TRoot>(), cancellation);
    }

    public string CreateTempTableForCopying()
    {
        return _inner.CreateTempTableForCopying();
    }

    public void LoadIntoTempTable(Tenant tenant, ISerializer serializer, NpgsqlConnection conn,
        IEnumerable<T> documents)
    {
        _inner.LoadIntoTempTable(tenant, serializer, conn, documents.OfType<TRoot>());
    }

    public Task LoadIntoTempTableAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn,
        IEnumerable<T> documents,
        CancellationToken cancellation)
    {
        return _inner.LoadIntoTempTableAsync(tenant, serializer, conn, documents.OfType<TRoot>(), cancellation);
    }

    public string CopyNewDocumentsFromTempTable()
    {
        return _inner.CopyNewDocumentsFromTempTable();
    }

    public string UpsertFromTempTable()
    {
        return _inner.UpsertFromTempTable();
    }
}
