#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal.Sessions;

namespace Marten;

public interface IAdvancedSql
{
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
    Task<IReadOnlyList<T>> QueryAsync<T>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameter can be a document class, a scalar or any JSON-serializable class.
    ///     If the result is a document, the SQL must contain a select with the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     selected.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<T>> QueryAsync<T>(char placeholder, string sql, CancellationToken token, params object[] parameters);

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
    Task<IReadOnlyList<(T1, T2)>> QueryAsync<T1, T2>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<(T1, T2)>> QueryAsync<T1, T2>(char placeholder, string sql, CancellationToken token, params object[] parameters);

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
    Task<IReadOnlyList<(T1, T2, T3)>> QueryAsync<T1, T2, T3>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <typeparam name="T3"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns></returns>
    Task<IReadOnlyList<(T1, T2, T3)>> QueryAsync<T1, T2, T3>(char placeholder, string sql, CancellationToken token, params object[] parameters);

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
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<T> Query<T>(string sql, params object[] parameters);

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
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<(T1, T2)> Query<T1, T2>(string sql, params object[] parameters);

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
    [Obsolete(QuerySession.SynchronousRemoval)]
    IReadOnlyList<(T1, T2, T3)> Query<T1, T2, T3>(string sql, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns>An async enumerable iterating over the results</returns>
    IAsyncEnumerable<T> StreamAsync<T>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns>An async enumerable iterating over the results</returns>
    IAsyncEnumerable<T> StreamAsync<T>(char placeholder, string sql, CancellationToken token, params object[] parameters);

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
    /// <returns>An async enumerable iterating over the list of result tuples</returns>
    IAsyncEnumerable<(T1, T2)> StreamAsync<T1, T2>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns>An async enumerable iterating over the list of result tuples</returns>
    IAsyncEnumerable<(T1, T2)> StreamAsync<T1, T2>(char placeholder, string sql, CancellationToken token, params object[] parameters);

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
    /// <returns>An async enumerable iterating over the list of result tuples</returns>
    IAsyncEnumerable<(T1, T2, T3)> StreamAsync<T1, T2, T3>(string sql, CancellationToken token, params object[] parameters);

    /// <summary>
    ///     Asynchronously queries the document storage with the supplied SQL.
    ///     The type parameters can be any document class, scalar or JSON-serializable class.
    ///     For each result type parameter, the SQL SELECT statement must contain a ROW.
    ///     For document types, the row must contain the required fields in the correct order,
    ///     depending on the session type and the metadata the document might use, at least id and data must be
    ///     provided.
    ///     Use <paramref name="placeholder"/> to specify a character that will be replaced by positional parameters.
    /// </summary>
    /// <typeparam name="T1"></typeparam>
    /// <typeparam name="T2"></typeparam>
    /// <typeparam name="T3"></typeparam>
    /// <param name="placeholder"></param>
    /// <param name="sql"></param>
    /// <param name="parameters"></param>
    /// <returns>An async enumerable iterating over the list of result tuples</returns>
    IAsyncEnumerable<(T1, T2, T3)> StreamAsync<T1, T2, T3>(char placeholder, string sql, CancellationToken token, params object[] parameters);
}
