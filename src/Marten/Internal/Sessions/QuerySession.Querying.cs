#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Services.BatchQuerying;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    private int _tableNumber;

    public string NextTempTableName()
    {
        return LinqConstants.IdListTableName + ++_tableNumber;
    }

    public IMartenQueryable<T> Query<T>()
    {
        return new MartenLinqQueryable<T>(this);
    }

    public IMartenQueryable<T> QueryForNonStaleData<T>(TimeSpan timeout)
    {
        var queryable = new MartenLinqQueryable<T>(this);
        queryable.MartenProvider.Waiter = new WaitForAggregate(timeout);

        return queryable;
    }

    public IReadOnlyList<T> Query<T>(string sql, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new UserSuppliedQueryHandler<T>(this, DefaultParameterPlaceholder, sql, parameters);

        if (!handler.SqlContainsCustomSelect)
        {
            Database.EnsureStorageExists(typeof(T));
        }

        var provider = new MartenLinqQueryProvider(this, typeof(T));
        return provider.ExecuteHandler(handler);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters)
    {
        return QueryAsync<T>(DefaultParameterPlaceholder, sql, token, parameters);
    }

    public async Task<IReadOnlyList<T>> QueryAsync<T>(char placeholder, string sql, CancellationToken token, params object[] parameters)
    {
        assertNotDisposed();

        var handler = new UserSuppliedQueryHandler<T>(this, placeholder, sql, parameters);

        if (!handler.SqlContainsCustomSelect)
        {
            await Database.EnsureStorageExistsAsync(typeof(T), token).ConfigureAwait(false);
        }

        var provider = new MartenLinqQueryProvider(this, typeof(T));
        return await provider.ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(string sql, params object[] parameters)
    {
        return QueryAsync<T>(sql, CancellationToken.None, parameters);
    }

    public Task<IReadOnlyList<T>> QueryAsync<T>(char placeholder, string sql, params object[] parameters)
    {
        return QueryAsync<T>(placeholder, sql, CancellationToken.None, parameters);
    }

    // TODO -- Obsolete, remove in 8.0, replaced by AdvancedSql.Query*
    #region Obsolete AdvancedSqlQuery*
    public Task<IReadOnlyList<T>> AdvancedSqlQueryAsync<T>(string sql, CancellationToken token,
        params object[] parameters) => ((IAdvancedSql)this).QueryAsync<T>(sql, token, parameters);

    public Task<IReadOnlyList<(T1, T2)>> AdvancedSqlQueryAsync<T1, T2>(string sql, CancellationToken token,
        params object[] parameters) => ((IAdvancedSql)this).QueryAsync<T1, T2>(sql, token, parameters);

    public Task<IReadOnlyList<(T1, T2, T3)>> AdvancedSqlQueryAsync<T1, T2, T3>(string sql, CancellationToken token,
        params object[] parameters) => ((IAdvancedSql)this).QueryAsync<T1, T2, T3>(sql, token, parameters);

    public IReadOnlyList<T> AdvancedSqlQuery<T>(string sql, params object[] parameters) =>
        ((IAdvancedSql)this).Query<T>(sql, parameters);

    public IReadOnlyList<(T1, T2)> AdvancedSqlQuery<T1, T2>(string sql, params object[] parameters) =>
        ((IAdvancedSql)this).Query<T1, T2>(sql, parameters);

    public IReadOnlyList<(T1, T2, T3)> AdvancedSqlQuery<T1, T2, T3>(string sql, params object[] parameters) =>
        ((IAdvancedSql)this).Query<T1, T2, T3>(sql, parameters);
    #endregion

    public IBatchedQuery CreateBatchQuery()
    {
        return new BatchedQuery(this);
    }

    public TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query)
    {
        var source = _store.GetCompiledQuerySourceFor(query, this);
        Database.EnsureStorageExists(typeof(TDoc));
        var handler = (IQueryHandler<TOut>)source.Build(query, this);

        return ExecuteHandler(handler);
    }

    public async Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default)
    {
        var source = _store.GetCompiledQuerySourceFor(query, this);
        await Database.EnsureStorageExistsAsync(typeof(TDoc), token).ConfigureAwait(false);
        var handler = (IQueryHandler<TOut>)source.Build(query, this);

        return await ExecuteHandlerAsync(handler, token).ConfigureAwait(false);
    }
}
