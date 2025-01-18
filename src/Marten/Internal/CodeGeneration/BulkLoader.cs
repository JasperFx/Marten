using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal.Storage;
using Marten.Schema.BulkLoading;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.CodeGeneration;

public abstract class BulkLoader<T, TId>: IBulkLoader<T>
{
    private readonly IDocumentStorage<T, TId> _storage;

    public BulkLoader(IDocumentStorage<T, TId> storage)
    {
        _storage = storage;
    }

    public void Load(Tenant tenant, ISerializer serializer, NpgsqlConnection conn, IEnumerable<T> documents)
    {
        using var writer = conn.BeginBinaryImport(MainLoaderSql());

        foreach (var document in documents)
        {
            _storage.AssignIdentity(document, tenant.TenantId, tenant.Database);
            writer.StartRow();
            LoadRow(writer, document, tenant, serializer);
        }

        writer.Complete();
    }

    public async Task LoadAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn,
        IEnumerable<T> documents,
        CancellationToken cancellation)
    {
        await using var writer = await conn.BeginBinaryImportAsync(MainLoaderSql(), cancellation).ConfigureAwait(false);

        foreach (var document in documents)
        {
            _storage.AssignIdentity(document, tenant.TenantId, tenant.Database);
            await writer.StartRowAsync(cancellation).ConfigureAwait(false);
            await LoadRowAsync(writer, document, tenant, serializer, cancellation).ConfigureAwait(false);
        }

        await writer.CompleteAsync(cancellation).ConfigureAwait(false);
    }


    public abstract string CreateTempTableForCopying();

    public void LoadIntoTempTable(Tenant tenant, ISerializer serializer, NpgsqlConnection conn,
        IEnumerable<T> documents)
    {
        using var writer = conn.BeginBinaryImport(TempLoaderSql());
        foreach (var document in documents)
        {
            writer.StartRow();
            LoadRow(writer, document, tenant, serializer);
        }

        writer.Complete();
    }

    public async Task LoadIntoTempTableAsync(Tenant tenant, ISerializer serializer, NpgsqlConnection conn,
        IEnumerable<T> documents,
        CancellationToken cancellation)
    {
        await using var writer = await conn.BeginBinaryImportAsync(TempLoaderSql(), cancellation).ConfigureAwait(false);
        foreach (var document in documents)
        {
            await writer.StartRowAsync(cancellation).ConfigureAwait(false);
            await LoadRowAsync(writer, document, tenant, serializer, cancellation).ConfigureAwait(false);
        }

        await writer.CompleteAsync(cancellation).ConfigureAwait(false);
    }

    public abstract string CopyNewDocumentsFromTempTable();

    public abstract string UpsertFromTempTable();

    public object GetNullable<TValue>(TValue? value) where TValue : struct
    {
        return value.HasValue ? value.Value : DBNull.Value;
    }

    public int GetEnumIntValue<TEnum>(TEnum? value) where TEnum : struct
    {
        if (value.HasValue)
        {
            return value.Value.As<int>();
        }

        return 0;
    }

    public string GetEnumStringValue<TEnum>(TEnum? value) where TEnum : struct
    {
        if (value.HasValue)
        {
            return value.Value.ToString();
        }

        return "EMPTY";
    }

    public abstract void LoadRow(NpgsqlBinaryImporter writer, T document, Tenant tenant, ISerializer serializer);

    public abstract Task LoadRowAsync(NpgsqlBinaryImporter writer, T document, Tenant tenant,
        ISerializer serializer, CancellationToken cancellation);


    public abstract string MainLoaderSql();
    public abstract string TempLoaderSql();
}
