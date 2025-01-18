using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Storage;
using Npgsql;

namespace Marten.Schema.BulkLoading;

/// <summary>
///     Internal service to implement bulk loading
/// </summary>
/// <typeparam name="T"></typeparam>
public interface IBulkLoader<T>
{
    void Load(Tenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);

    Task LoadAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
        CancellationToken cancellation);

    string CreateTempTableForCopying();

    void LoadIntoTempTable(Tenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents);

    Task LoadIntoTempTableAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents,
        CancellationToken cancellation);

    string CopyNewDocumentsFromTempTable();

    string UpsertFromTempTable();
}
