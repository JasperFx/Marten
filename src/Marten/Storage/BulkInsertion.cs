using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using JasperFx.Core.Reflection;
using Marten.Schema.BulkLoading;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Storage;

internal class BulkInsertion: IDisposable
{
    private readonly Tenant _tenant;

    public BulkInsertion(Tenant tenant, StoreOptions options)
    {
        _tenant = tenant;
        Serializer = options.Serializer();
    }

    public ISerializer Serializer { get; }

    public void Dispose()
    {
    }

    public void BulkInsert<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, string updateCondition = null)
    {
        ValidateupdateCondition<T>(mode, updateCondition);
        if (typeof(T) == typeof(object))
        {
            BulkInsertDocuments(documents.OfType<object>(), mode);
        }
        else
        {
            _tenant.Database.EnsureStorageExists(typeof(T));

            using var conn = _tenant.Database.CreateConnection();
            conn.Open();
            var tx = conn.BeginTransaction();

            try
            {
                bulkInsertDocuments(documents, batchSize, conn, mode, updateCondition);

                tx.Commit();
            }
            catch (Exception)
            {
                tx.Rollback();
                throw;
            }
        }
    }

    public void BulkInsertEnlistTransaction<T>(IReadOnlyCollection<T> documents,
        Transaction transaction,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000,
        string updateCondition = null)
    {
        ValidateupdateCondition<T>(mode, updateCondition);
        if (typeof(T) == typeof(object))
        {
            BulkInsertDocumentsEnlistTransaction(documents.OfType<object>(), transaction, mode);
        }
        else
        {
            _tenant.Database.EnsureStorageExists(typeof(T));

            using var conn = _tenant.Database.CreateConnection();
            conn.Open();
            conn.EnlistTransaction(transaction);
            bulkInsertDocuments(documents, batchSize, conn, mode, updateCondition);
        }
    }

    public async Task BulkInsertAsync<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode, int batchSize,
        string updateCondition = null, CancellationToken cancellation = default)
    {
        ValidateupdateCondition<T>(mode, updateCondition);
        if (typeof(T) == typeof(object))
        {
            await BulkInsertDocumentsAsync(documents.OfType<object>(), mode, batchSize, cancellation)
                .ConfigureAwait(false);
        }
        else
        {
            await _tenant.Database.EnsureStorageExistsAsync(typeof(T), cancellation).ConfigureAwait(false);

            await using var conn = _tenant.Database.CreateConnection();
            await conn.OpenAsync(cancellation).ConfigureAwait(false);

            var tx = await conn.BeginTransactionAsync(cancellation).ConfigureAwait(false);
            try
            {
                await bulkInsertDocumentsAsync(documents, batchSize, conn, mode, updateCondition, cancellation).ConfigureAwait(false);

                await tx.CommitAsync(cancellation).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await tx.RollbackAsync(cancellation).ConfigureAwait(false);
                throw;
            }
        }
    }

    public async Task BulkInsertEnlistTransactionAsync<T>(IReadOnlyCollection<T> documents, Transaction transaction,
        BulkInsertMode mode, int batchSize, string updateCondition = null, CancellationToken cancellation = default)
    {
        ValidateupdateCondition<T>(mode, updateCondition);
        if (typeof(T) == typeof(object))
        {
            await BulkInsertDocumentsEnlistTransactionAsync(documents.OfType<object>(), transaction, mode, batchSize,
                cancellation).ConfigureAwait(false);
        }
        else
        {
            await _tenant.Database.EnsureStorageExistsAsync(typeof(T), cancellation).ConfigureAwait(false);
            await using var conn = _tenant.Database.CreateConnection();
            await conn.OpenAsync(cancellation).ConfigureAwait(false);
            conn.EnlistTransaction(transaction);
            await bulkInsertDocumentsAsync(documents, batchSize, conn, mode, updateCondition, cancellation).ConfigureAwait(false);
        }
    }

    public void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, string updateCondition = null)
    {
        var groups = bulkInserters(documents);

        using var conn = _tenant.Database.CreateConnection();

        conn.Open();
        var tx = conn.BeginTransaction();

        try
        {
            foreach (var group in groups) group.BulkInsert(batchSize, conn, this, mode);

            tx.Commit();
        }
        catch (Exception)
        {
            tx.Rollback();
            throw;
        }
    }

    public void BulkInsertDocumentsEnlistTransaction(IEnumerable<object> documents,
        Transaction transaction,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000,
        string updateCondition = null)
    {
        var groups = bulkInserters(documents);
        var types = documentTypes(documents);

        // this needs to be done before open connection
        foreach (var type in types)
            _tenant.Database.EnsureStorageExists(type);

        using var conn = _tenant.Database.CreateConnection();
        conn.Open();
        conn.EnlistTransaction(transaction);

        foreach (var group in groups)
            group.BulkInsert(batchSize, conn, this, mode);
    }

    private static Type[] documentTypes(IEnumerable<object> documents)
    {
        return documents.Where(x => x != null)
            .GroupBy(x => x.GetType())
            .Select(x => x.Key)
            .ToArray();
    }

    private static IBulkInserter[] bulkInserters(IEnumerable<object> documents)
    {
        return documents.Where(x => x != null)
            .GroupBy(x => x.GetType())
            .Select(group => typeof(BulkInserter<>).CloseAndBuildAs<IBulkInserter>(group, group.Key))
            .ToArray();
    }

    public async Task BulkInsertDocumentsAsync(IEnumerable<object> documents, BulkInsertMode mode, int batchSize,
        CancellationToken cancellation = default)
    {
        var groups = bulkInserters(documents);

        await using var conn = _tenant.Database.CreateConnection();

        await conn.OpenAsync(cancellation).ConfigureAwait(false);
        var tx = await conn.BeginTransactionAsync(cancellation).ConfigureAwait(false);

        try
        {
            foreach (var group in groups)
                await group.BulkInsertAsync(batchSize, conn, this, mode, cancellation).ConfigureAwait(false);

            await tx.CommitAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await tx.RollbackAsync(cancellation).ConfigureAwait(false);
            throw;
        }
    }

    public async Task BulkInsertDocumentsEnlistTransactionAsync(
        IEnumerable<object> documents,
        Transaction transaction,
        BulkInsertMode mode,
        int batchSize,
        CancellationToken cancellation
    )
    {
        var groups = bulkInserters(documents);
        var types = documentTypes(documents);

        // this needs to be done before open connection
        foreach (var type in types)
            await _tenant.Database.EnsureStorageExistsAsync(type, cancellation).ConfigureAwait(false);

        await using var conn = _tenant.Database.CreateConnection();
        await conn.OpenAsync(cancellation).ConfigureAwait(false);
        conn.EnlistTransaction(transaction);

        foreach (var group in groups)
            await group.BulkInsertAsync(batchSize, conn, this, mode, cancellation).ConfigureAwait(false);
    }

    private void bulkInsertDocuments<T>(IReadOnlyCollection<T> documents, int batchSize, NpgsqlConnection conn,
        BulkInsertMode mode, string updateCondition)
    {
        var provider = _tenant.Database.Providers.StorageFor<T>();
        var loader = provider.BulkLoader!;

        if (mode != BulkInsertMode.InsertsOnly)
        {
            var sql = loader.CreateTempTableForCopying();
            conn.CreateCommand(sql).ExecuteNonQuery();
        }

        if (documents.Count <= batchSize)
        {
            loadDocuments(documents, loader, mode, conn);
        }
        else
        {
            var batch = new List<T>(batchSize);

            foreach (var document in documents)
            {
                batch.Add(document);

                if (batch.Count < batchSize)
                {
                    continue;
                }

                loadDocuments(batch, loader, mode, conn);
                batch.Clear();
            }

            loadDocuments(batch, loader, mode, conn);
        }

        if (mode == BulkInsertMode.IgnoreDuplicates)
        {
            var copy = loader.CopyNewDocumentsFromTempTable();

            conn.CreateCommand(copy).ExecuteNonQuery();
        }
        else if (mode == BulkInsertMode.OverwriteExisting)
        {
            var upsert = string.Format(loader.UpsertFromTempTable(), updateCondition ?? "true");

            conn.CreateCommand(upsert).ExecuteNonQuery();
        }
    }

    private async Task bulkInsertDocumentsAsync<T>(IReadOnlyCollection<T> documents, int batchSize,
        NpgsqlConnection conn, BulkInsertMode mode, string updateCondition, CancellationToken cancellation)
    {
        var provider = _tenant.Database.Providers.StorageFor<T>();
        var loader = provider.BulkLoader!;

        if (mode != BulkInsertMode.InsertsOnly)
        {
            var sql = loader.CreateTempTableForCopying();
            await conn.CreateCommand(sql).ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
        }

        if (documents.Count <= batchSize)
        {
            await loadDocumentsAsync(documents, loader, mode, conn, cancellation).ConfigureAwait(false);
        }
        else
        {
            var batch = new List<T>(batchSize);

            foreach (var document in documents)
            {
                batch.Add(document);

                if (batch.Count < batchSize)
                {
                    continue;
                }

                await loadDocumentsAsync(batch, loader, mode, conn, cancellation).ConfigureAwait(false);
                batch.Clear();
            }

            await loadDocumentsAsync(batch, loader, mode, conn, cancellation).ConfigureAwait(false);
        }

        if (mode == BulkInsertMode.IgnoreDuplicates)
        {
            var copy = loader.CopyNewDocumentsFromTempTable();

            await conn.CreateCommand(copy).ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
        }
        else if (mode == BulkInsertMode.OverwriteExisting)
        {
            var upsert = string.Format(loader.UpsertFromTempTable(), updateCondition ?? "true");


            await conn.CreateCommand(upsert).ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
        }
    }

    private static void ValidateupdateCondition<T>(BulkInsertMode mode, string updateCondition)
    {
        if (updateCondition is null)
        {
            return;
        }

        if (typeof(T) == typeof(object))
        {
            throw new ArgumentException($"An update condition can not be used on a collection of <object>, use a collection of a specific document type instead.", nameof(updateCondition));
        }

        if (mode != BulkInsertMode.OverwriteExisting)
        {
            throw new ArgumentException($"An update condition can only be provided when using {BulkInsertMode.OverwriteExisting}", nameof(updateCondition));
        }
    }

    private void loadDocuments<T>(IEnumerable<T> documents, IBulkLoader<T> loader, BulkInsertMode mode,
        NpgsqlConnection conn)
    {
        if (mode == BulkInsertMode.InsertsOnly)
        {
            loader.Load(_tenant, Serializer, conn, documents);
        }
        else
        {
            loader.LoadIntoTempTable(_tenant, Serializer, conn, documents);
        }
    }

    private async Task loadDocumentsAsync<T>(IReadOnlyCollection<T> documents, IBulkLoader<T> loader,
        BulkInsertMode mode, NpgsqlConnection conn, CancellationToken cancellation)
    {
        if (mode == BulkInsertMode.InsertsOnly)
        {
            await loader.LoadAsync(_tenant, Serializer, conn, documents, cancellation).ConfigureAwait(false);
        }
        else
        {
            await loader.LoadIntoTempTableAsync(_tenant, Serializer, conn, documents, cancellation)
                .ConfigureAwait(false);
        }
    }

    internal interface IBulkInserter
    {
        void BulkInsert(int batchSize, NpgsqlConnection connection, BulkInsertion parent, BulkInsertMode mode);

        Task BulkInsertAsync(int batchSize, NpgsqlConnection conn, BulkInsertion bulkInsertion, BulkInsertMode mode,
            CancellationToken cancellation);
    }

    internal class BulkInserter<T>: IBulkInserter
    {
        private readonly T[] _documents;

        public BulkInserter(IEnumerable<object> documents)
        {
            _documents = documents.OfType<T>().ToArray();
        }

        public void BulkInsert(int batchSize, NpgsqlConnection connection, BulkInsertion parent,
            BulkInsertMode mode)
        {
            parent._tenant.Database.EnsureStorageExists(typeof(T));
            parent.bulkInsertDocuments(_documents, batchSize, connection, mode, null);
        }

        public async Task BulkInsertAsync(int batchSize, NpgsqlConnection conn, BulkInsertion parent,
            BulkInsertMode mode,
            CancellationToken cancellation)
        {
            await parent._tenant.Database.EnsureStorageExistsAsync(typeof(T), cancellation).ConfigureAwait(false);
            await parent.bulkInsertDocumentsAsync(_documents, batchSize, conn, mode, null, cancellation)
                .ConfigureAwait(false);
        }
    }
}
