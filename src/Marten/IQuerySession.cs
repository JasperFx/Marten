#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Schema;
using Marten.Services.BatchQuerying;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten;

public interface IQuerySession: IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     The underlying Marten database for this session
    /// </summary>
    IMartenDatabase Database { get; }

    /// <summary>
    ///     The currently open Npgsql connection for this session. Use with caution.
    /// </summary>
    NpgsqlConnection Connection { get; }

    /// <summary>
    ///     The session specific logger for this session. Can be set for better integration
    ///     with custom diagnostics
    /// </summary>
    IMartenSessionLogger Logger { get; set; }

    /// <summary>
    ///     Request count
    /// </summary>
    int RequestCount { get; }

    /// <summary>
    ///     The document store that created this session
    /// </summary>
    IDocumentStore DocumentStore { get; }

    /// <summary>
    ///     Access to the event store functionality
    /// </summary>
    IQueryEventStore Events { get; }

    /// <summary>
    ///     Directly load the persisted JSON data for documents by Id
    /// </summary>
    IJsonLoader Json { get; }

    /// <summary>
    ///     Optional metadata describing the causation id for this
    ///     unit of work
    /// </summary>
    string? CausationId { get; set; }

    /// <summary>
    ///     Optional metadata describing the correlation id for this
    ///     unit of work
    /// </summary>
    string? CorrelationId { get; set; }

    /// <summary>
    /// The tenant id for this session. If not opened with a tenant id, this value will be "*DEFAULT*"
    /// </summary>
    string TenantId { get; }

    /// <summary>
    ///     Find or load a single document of type T by a string id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    T? Load<T>(string id) where T : notnull;

    /// <summary>
    ///     Asynchronously find or load a single document of type T by a string id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> LoadAsync<T>(string id, CancellationToken token = default) where T : notnull;

    /// <summary>
    /// Asynchronously find or load a single document of type T by a user supplied id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> LoadAsync<T>(object id, CancellationToken token = default) where T : notnull;

    /// <summary>
    ///     Load or find a single document of type T with either a numeric or Guid id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    T? Load<T>(int id) where T : notnull;

    /// <summary>
    ///     Load or find a single document of type T with either a numeric or Guid id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    T? Load<T>(long id) where T : notnull;

    /// <summary>
    ///     Load or find a single document of type T with either a numeric or Guid id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    T? Load<T>(Guid id) where T : notnull;

    /// <summary>
    ///     Asynchronously load or find a single document of type T with either a numeric or Guid id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> LoadAsync<T>(int id, CancellationToken token = default) where T : notnull;

    /// <summary>
    ///     Asynchronously load or find a single document of type T with either a numeric or Guid id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> LoadAsync<T>(long id, CancellationToken token = default) where T : notnull;

    /// <summary>
    ///     Asynchronously load or find a single document of type T with either a numeric or Guid id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="id"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<T?> LoadAsync<T>(Guid id, CancellationToken token = default) where T : notnull;

    #region sample_querying_with_linq

    /// <summary>
    ///     Use Linq operators to query the documents
    ///     stored in Postgresql
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> Query<T>();

    #endregion

    /// <summary>
    /// If you are querying for data against a projected event aggregation that is updated asynchronously
    /// through the async daemon, this method will ensure that you are querying against the latest events appended
    /// to the system by waiting for the aggregate to catch up to the current "high water mark" of the event store
    /// at the time this query is executed.
    /// </summary>
    /// <param name="timeout"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    IMartenQueryable<T> QueryForNonStaleData<T>(TimeSpan timeout);

    /// <summary>
    ///     Queries the document storage table for the document type T by supplied SQL. See
    ///     https://martendb.io/documents/querying/sql.html for more information on usage.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> Query<T>(string sql, params object[] parameters);

    /// <summary>
    ///     Stream the results of a user-supplied query directly to a stream as a JSON array
    /// </summary>
    /// <param name="destination"></param>
    /// <param name="token"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<int> StreamJson<T>(Stream destination, CancellationToken token, string sql, params object[] parameters);

    /// <summary>
    ///     Stream the results of a user-supplied query directly to a stream as a JSON array.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <param name="destination"></param>
    /// <param name="token"></param>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<int> StreamJson<T>(Stream destination, CancellationToken token, char placeholder, string sql, params object[] parameters);

    /// <summary>
    ///     Stream the results of a user-supplied query directly to a stream as a JSON array
    /// </summary>
    /// <param name="destination"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<int> StreamJson<T>(Stream destination, string sql, params object[] parameters);

    /// <summary>
    ///     Stream the results of a user-supplied query directly to a stream as a JSON array.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <param name="destination"></param>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<int> StreamJson<T>(Stream destination, char placeholder, string sql, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage table for the document type T by supplied SQL. See
    ///     https://martendb.io/documents/querying/sql.html for more information on usage.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="token"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage table for the document type T by supplied SQL. See
    ///     https://martendb.io/documents/querying/sql.html for more information on usage.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="token"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<T>> QueryAsync<T>(char placeholder, string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage table for the document type T by supplied SQL. See
    ///     https://martendb.io/documents/querying/sql.html for more information on usage.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage table for the document type T by supplied SQL. See
    ///     https://martendb.io/documents/querying/sql.html for more information on usage.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<T>> QueryAsync<T>(char placeholder, string sql, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameter can be a document class, a scalar or any JSON-serializable class.
    ///     If the result is a document, the SQL must contain a select with the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     selected.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Use AdvancedSql.QueryAsync<T>(...) instead.")]
    Task<IReadOnlyList<T>> AdvancedSqlQueryAsync<T>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Use AdvancedSql.QueryAsync<T1, T2>(...) instead.")]
    Task<IReadOnlyList<(T1, T2)>> AdvancedSqlQueryAsync<T1, T2>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <typeparam name="T3"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Use AdvancedSql.QueryAsync<T1, T2, T3>(...) instead.")]
    Task<IReadOnlyList<(T1, T2, T3)>> AdvancedSqlQueryAsync<T1, T2,T3>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameter can be a document class, a scalar or any JSON-serializable class.
    ///     If the result is a document, the SQL must contain a select with the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     selected.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Use AdvancedSql.QueryAsync<T>(...) instead.")]
    IReadOnlyList<T> AdvancedSqlQuery<T>(string sql, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Use AdvancedSql.QueryAsync<T1, T2>(...) instead.")]
    IReadOnlyList<(T1, T2)> AdvancedSqlQuery<T1, T2>(string sql, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <typeparam name="T3"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    [Obsolete("Will be removed in 8.0. Use AdvancedSql.QueryAsync<T1, T2, T3>(...) instead.")]
    IReadOnlyList<(T1, T2, T3)> AdvancedSqlQuery<T1, T2, T3>(string sql, params object[] parameters);

    /// <summary>
    ///     Define a batch of deferred queries and load operations to be conducted in one asynchronous request to the
    ///     database for potentially performance
    /// </summary>
    /// <returns></returns>
    IBatchedQuery CreateBatchQuery();

    /// <summary>
    ///     A query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    /// <typeparam name="TOut">The output</typeparam>
    /// <param name="query">The instance of a compiled query</param>
    /// <returns>A single item query result</returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    TOut Query<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query);

    /// <summary>
    ///     An async query that is compiled so a copy of the DbCommand can be used directly in subsequent requests.
    /// </summary>
    /// <typeparam name="TDoc">The document</typeparam>
    /// <typeparam name="TOut">The output</typeparam>
    /// <param name="query">The instance of a compiled query</param>
    /// <param name="token">A cancellation token</param>
    /// <returns>A task for a single item query result</returns>
    Task<TOut> QueryAsync<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default);

    /// <summary>
    ///     Stream a single JSON document to the destination using a compiled query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="destination"></param>
    /// <param name="token"></param>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <returns></returns>
    Task<bool> StreamJsonOne<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, Stream destination,
        CancellationToken token = default);

    /// <summary>
    ///     Stream many documents as a JSON array to the destination using a compiled query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="destination"></param>
    /// <param name="token"></param>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <returns></returns>
    Task<int> StreamJsonMany<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, Stream destination,
        CancellationToken token = default);

    /// <summary>
    ///     Fetch the JSON representation of a single document using a compiled query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="token"></param>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <returns></returns>
    Task<string?> ToJsonOne<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default);

    /// <summary>
    ///     Fetch the JSON array representation of a list of documents using a compiled query
    /// </summary>
    /// <param name="query"></param>
    /// <param name="token"></param>
    /// <typeparam name="TDoc"></typeparam>
    /// <typeparam name="TOut"></typeparam>
    /// <returns></returns>
    Task<string> ToJsonMany<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, CancellationToken token = default);

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(params string[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(IEnumerable<string> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(params Guid[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(IEnumerable<Guid> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(params int[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(IEnumerable<int> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(params long[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> LoadMany<T>(IEnumerable<long> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(params string[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<string> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(params Guid[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<Guid> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(params int[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<int> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(params long[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(IEnumerable<long> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params string[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<string> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params Guid[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<Guid> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params int[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<int> ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, params long[] ids) where T : notnull;

    /// <summary>
    ///     Load or find multiple documents by id
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<IReadOnlyList<T>> LoadManyAsync<T>(CancellationToken token, IEnumerable<long> ids) where T : notnull;

    /// <summary>
    ///     Retrieve the current known version of the given document
    ///     according to this session. Will return null if the document is
    ///     not part of this session
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    Guid? VersionFor<TDoc>(TDoc entity) where TDoc : notnull;

    /// <summary>
    ///     Performs a full text search against <typeparamref name="TDoc" />
    /// </summary>
    /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'to_tsquery' function, must match the config parameter used
    ///     by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<TDoc> Search<TDoc>(string queryText, string regConfig = FullTextIndexDefinition.DefaultRegConfig);

    /// <summary>
    ///     Performs an asynchronous full text search against <typeparamref name="TDoc" />
    /// </summary>
    /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'to_tsquery' function, must match the config parameter used
    ///     by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    Task<IReadOnlyList<TDoc>> SearchAsync<TDoc>(string queryText,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig,
        CancellationToken token = default);

    /// <summary>
    ///     Performs a full text search against <typeparamref name="TDoc" /> using the 'plainto_tsquery' search function
    /// </summary>
    /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'to_tsquery' function, must match the config parameter used
    ///     by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    IReadOnlyList<TDoc> PlainTextSearch<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig);

    /// <summary>
    ///     Performs an asynchronous full text search against <typeparamref name="TDoc" /> using the 'plainto_tsquery' function
    /// </summary>
    /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'to_tsquery' function, must match the config parameter used
    ///     by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    Task<IReadOnlyList<TDoc>> PlainTextSearchAsync<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig, CancellationToken token = default);

    /// <summary>
    ///     Performs a full text search against <typeparamref name="TDoc" /> using the 'phraseto_tsquery' search function
    /// </summary>
    /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'to_tsquery' function, must match the config parameter used
    ///     by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    IReadOnlyList<TDoc> PhraseSearch<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig);

    /// <summary>
    ///     Performs an asynchronous full text search against <typeparamref name="TDoc" /> using the 'phraseto_tsquery' search
    ///     function
    /// </summary>
    /// <param name="queryText">The text to search for.  May contain lexeme patterns used by PostgreSQL for full text searching</param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'to_tsquery' function, must match the config parameter used
    ///     by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    Task<IReadOnlyList<TDoc>> PhraseSearchAsync<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig, CancellationToken token = default);

    /// <summary>
    ///     Performs a full text search against <typeparamref name="TDoc" /> using the 'websearch_to_tsquery' search function
    /// </summary>
    /// <param name="searchTerm">
    ///     The text to search for.  Uses an alternative syntax to the other search functions, similar to
    ///     the one used by web search engines
    /// </param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'websearch_to_tsquery' function, must match the config
    ///     parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <remarks>
    ///     Supported from Postgres 11
    ///     See: https://www.postgresql.org/docs/11/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    IReadOnlyList<TDoc> WebStyleSearch<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig);

    /// <summary>
    ///     Performs an asynchronous full text search against <typeparamref name="TDoc" /> using the 'websearch_to_tsquery'
    ///     search function
    /// </summary>
    /// <param name="searchTerm">
    ///     The text to search for.  Uses an alternative syntax to the other search functions, similar to
    ///     the one used by web search engines
    /// </param>
    /// <param name="regConfig">
    ///     The dictionary config passed to the 'websearch_to_tsquery' function, must match the config
    ///     parameter used by <seealso cref="DocumentMapping.AddFullTextIndex(string)" />
    /// </param>
    /// <param name="token"></param>
    /// <remarks>
    ///     Supported from Postgres 11
    ///     See: https://www.postgresql.org/docs/11/static/textsearch-controls.html#TEXTSEARCH-PARSING-QUERIES
    /// </remarks>
    Task<IReadOnlyList<TDoc>> WebStyleSearchAsync<TDoc>(string searchTerm,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig, CancellationToken token = default);


    /// <summary>
    ///     Fetch the entity version and last modified time from the database
    /// </summary>
    /// <param name="entity"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    DocumentMetadata? MetadataFor<T>(T entity) where T : notnull;

    /// <summary>
    ///     Fetch the entity version and last modified time from the database
    /// </summary>
    /// <param name="entity"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<DocumentMetadata> MetadataForAsync<T>(T entity,
        CancellationToken token = default) where T : notnull;

    /// <summary>
    ///     Access data from another tenant and apply document or event updates to this
    ///     IDocumentSession for a separate tenant
    /// </summary>
    /// <param name="tenantId"></param>
    /// <returns></returns>
    ITenantQueryOperations ForTenant(string tenantId);

    /// <summary>
    ///     Execute a single command against the database with this session's connection
    /// </summary>
    /// <param name="cmd"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    int Execute(NpgsqlCommand cmd);

    /// <summary>
    ///     Execute a single command against the database with this session's connection
    /// </summary>
    /// <param name="command"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<int> ExecuteAsync(NpgsqlCommand command, CancellationToken token = new());

    /// <summary>
    ///     Execute a single command against the database with this session's connection and return the results
    /// </summary>
    /// <param name="command"></param>
    /// <returns></returns>
    [Obsolete(QuerySession.SynchronousRemoval)]
    DbDataReader ExecuteReader(NpgsqlCommand command);

    /// <summary>
    ///     Execute a single command against the database with this session's connection and return the results
    /// </summary>
    /// <param name="command"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default);

    /// <summary>
    ///     Advanced sql query methods, to allow you to query the database
    ///     beyond what you can do with LINQ.
    /// </summary>
    IAdvancedSql AdvancedSql { get; }

    /// <summary>
    /// Use a query plan to execute a query
    /// </summary>
    /// <param name="plan"></param>
    /// <param name="token"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    Task<T> QueryByPlanAsync<T>(IQueryPlan<T> plan, CancellationToken token = default);


}
