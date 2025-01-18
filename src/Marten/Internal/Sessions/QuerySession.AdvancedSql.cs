using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Util;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Internal.Sessions;

public partial class QuerySession: IAdvancedSql
{
    Task<IReadOnlyList<T>> IAdvancedSql.QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
    {
        return ((IAdvancedSql)this).QueryAsync<T>(DefaultParameterPlaceholder, sql, token, parameters);
    }

    async Task<IReadOnlyList<T>> IAdvancedSql.QueryAsync<T>(char placeholder, string sql, CancellationToken token, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T>(this, placeholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var provider = new MartenLinqQueryProvider(this, typeof(T));
        return await provider.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    Task<IReadOnlyList<(T1, T2)>> IAdvancedSql.QueryAsync<T1, T2>(string sql, CancellationToken token, params object[] parameters)
    {
        return ((IAdvancedSql)this).QueryAsync<T1, T2>(DefaultParameterPlaceholder, sql, token, parameters);
    }

    async Task<IReadOnlyList<(T1, T2)>> IAdvancedSql.QueryAsync<T1, T2>(char placeholder, string sql, CancellationToken token, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2>(this, placeholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var provider = new MartenLinqQueryProvider(this, typeof((T1, T2)));
        return await provider.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    Task<IReadOnlyList<(T1, T2, T3)>> IAdvancedSql.QueryAsync<T1, T2, T3>(string sql, CancellationToken token, params object[] parameters)
    {
        return ((IAdvancedSql)this).QueryAsync<T1, T2, T3>(DefaultParameterPlaceholder, sql, token, parameters);
    }

    async Task<IReadOnlyList<(T1, T2, T3)>> IAdvancedSql.QueryAsync<T1, T2, T3>(char placeholder, string sql, CancellationToken token, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2, T3>(this, placeholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var provider = new MartenLinqQueryProvider(this, typeof((T1, T2, T3)));
        return await provider.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    IReadOnlyList<T> IAdvancedSql.Query<T>(string sql, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T>(this, DefaultParameterPlaceholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            Database.EnsureStorageExists(documentType);
        }

        var provider = new MartenLinqQueryProvider(this, typeof(T));
        return provider.ExecuteHandler(handler);
    }

    IReadOnlyList<(T1, T2)> IAdvancedSql.Query<T1, T2>(string sql, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2>(this, DefaultParameterPlaceholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            Database.EnsureStorageExists(documentType);
        }

        var provider = new MartenLinqQueryProvider(this, typeof((T1, T2)));
        return provider.ExecuteHandler(handler);
    }

    IReadOnlyList<(T1, T2, T3)> IAdvancedSql.Query<T1, T2, T3>(string sql, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2, T3>(this, DefaultParameterPlaceholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            Database.EnsureStorageExists(documentType);
        }

        var provider = new MartenLinqQueryProvider(this, typeof((T1, T2, T3)));
        return provider.ExecuteHandler(handler);
    }

    IAsyncEnumerable<T> IAdvancedSql.StreamAsync<T>(string sql, CancellationToken token, params object[] parameters)
        => ((IAdvancedSql)this).StreamAsync<T>(DefaultParameterPlaceholder, sql, token, parameters);

    async IAsyncEnumerable<T> IAdvancedSql.StreamAsync<T>(char placeholder, string sql, [EnumeratorCancellation] CancellationToken token,
        params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T>(this, placeholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var batch = this.BuildCommand(handler);
        await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);

        await foreach (var result in handler.EnumerateResults(reader, token))
        {
            yield return result;
        }
    }

    IAsyncEnumerable<(T1, T2)> IAdvancedSql.StreamAsync<T1, T2>(string sql, CancellationToken token, params object[] parameters)
    {
        return ((IAdvancedSql)this).StreamAsync<T1, T2>(DefaultParameterPlaceholder, sql, token, parameters);
    }

    async IAsyncEnumerable<(T1, T2)> IAdvancedSql.StreamAsync<T1, T2>(char placeholder, string sql, [EnumeratorCancellation] CancellationToken token,
        params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2>(this, placeholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var batch = this.BuildCommand(handler);
        await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);

        await foreach (var result in handler.EnumerateResults(reader, token))
        {
            yield return result;
        }
    }

    IAsyncEnumerable<(T1, T2, T3)> IAdvancedSql.StreamAsync<T1, T2, T3>(string sql, CancellationToken token, params object[] parameters)
    {
        return ((IAdvancedSql)this).StreamAsync<T1, T2, T3>(DefaultParameterPlaceholder, sql, token, parameters);
    }

    async IAsyncEnumerable<(T1, T2, T3)> IAdvancedSql.StreamAsync<T1, T2, T3>(char placeholder, string sql, [EnumeratorCancellation] CancellationToken token,
        params object[] parameters)
    {
        assertNotDisposed();

        var handler = new AdvancedSqlQueryHandler<T1, T2, T3>(this, placeholder, sql, parameters);

        foreach (var documentType in handler.DocumentTypes)
        {
            await Database.EnsureStorageExistsAsync(documentType, token).ConfigureAwait(false);
        }

        var batch = this.BuildCommand(handler);
        await using var reader = await ExecuteReaderAsync(batch, token).ConfigureAwait(false);

        await foreach (var result in handler.EnumerateResults(reader, token))
        {
            yield return result;
        }
    }
}
