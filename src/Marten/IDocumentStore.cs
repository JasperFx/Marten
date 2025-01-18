#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using Marten.Events.Daemon;
using Marten.Services;
using Microsoft.Extensions.Logging;
using IsolationLevel = System.Data.IsolationLevel;

namespace Marten;

/// <summary>
///     The core abstraction for a Marten document and event store. This should probably be scoped as a
///     singleton in your system
/// </summary>
public interface IDocumentStore: IDisposable
{
    /// <summary>
    ///     Information about the current configuration of this IDocumentStore
    /// </summary>
    IReadOnlyStoreOptions Options { get; }

    /// <summary>
    ///     Administration and diagnostic information about the underlying database storage
    /// </summary>
    IMartenStorage Storage { get; }

    /// <summary>
    ///     Infrequently used operations like document cleaning and the initial store configuration
    /// </summary>
    AdvancedOperations Advanced { get; }

    /// <summary>
    ///     Access to Marten's diagnostics for trouble shooting
    /// </summary>
    IDiagnostics Diagnostics { get; }

    /// <summary>
    ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
    ///     a large number of documents of type "T" to the database. This operation is transactional.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    /// <param name="updateCondition">
    ///     Raw sql that is used as the <c>WHERE</c> clause of the <c>ON CONFLICT DO UPDATE</c> statement when configured for <see cref="BulkInsertMode.OverwriteExisting"/>.
    ///     Use <c>d.</c> to refer to the existing record in the table and <c>excluded.</c> to refer to the conflicting record that was excluded from insertion.
    ///
    ///     <para>
    ///         Example:
    ///         <code>
    ///             "d.export_date &lt;= excluded.export_date"
    ///         </code>
    ///     </para>
    /// </param>
    void BulkInsert<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, string? updateCondition = null);

    /// <summary>
    ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
    ///     a large number of documents of type "T" to the database. This operation enlists an existing transaction.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="documents"></param>
    /// <param name="transaction">an existing transaction</param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    /// <param name="updateCondition">
    ///     Raw sql that is used as the <c>WHERE</c> clause of the <c>ON CONFLICT DO UPDATE</c> statement when configured for <see cref="BulkInsertMode.OverwriteExisting"/>.
    ///     Use <c>d.</c> to refer to the existing record in the table and <c>excluded.</c> to refer to the conflicting record that was excluded from insertion.
    ///
    ///     <para>
    ///         Example:
    ///         <code>
    ///             "d.export_date &lt;= excluded.export_date"
    ///         </code>
    ///     </para>
    /// </param>
    void BulkInsertEnlistTransaction<T>(IReadOnlyCollection<T> documents, Transaction transaction,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000, string? updateCondition = null);

    /// <summary>
    ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
    ///     a large number of documents of type "T" to the database. This operation is transactional.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="tenantId"></param>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="updateCondition">
    ///     Raw sql that is used as the <c>WHERE</c> clause of the <c>ON CONFLICT DO UPDATE</c> statement when configured for <see cref="BulkInsertMode.OverwriteExisting"/>.
    ///     Use <c>d.</c> to refer to the existing record in the table and <c>excluded.</c> to refer to the conflicting record that was excluded from insertion.
    ///
    ///     <para>
    ///         Example:
    ///         <code>
    ///             "d.export_date &lt;= excluded.export_date"
    ///         </code>
    ///     </para>
    /// </param>
    /// <param name="batchSize"></param>
    void BulkInsert<T>(string tenantId, IReadOnlyCollection<T> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000, string? updateCondition = null);

    /// <summary>
    ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
    ///     a large number of documents of type "T" to the database. This operation is transactional.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    /// <param name="updateCondition">
    ///     Raw sql that is used as the <c>WHERE</c> clause of the <c>ON CONFLICT DO UPDATE</c> statement when configured for <see cref="BulkInsertMode.OverwriteExisting"/>.
    ///     Use <c>d.</c> to refer to the existing record in the table and <c>excluded.</c> to refer to the conflicting record that was excluded from insertion.
    ///
    ///     <para>
    ///         Example:
    ///         <code>
    ///             "d.export_date &lt;= excluded.export_date"
    ///         </code>
    ///     </para>
    /// </param>
    Task BulkInsertAsync<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, string? updateCondition = null, CancellationToken cancellation = default);

    /// <summary>
    ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
    ///     a large number of documents of type "T" to the database. This operation enlists an existing transaction.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="documents"></param>
    /// <param name="transaction">an existing transaction</param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    /// <param name="updateCondition">
    ///     Raw sql that is used as the <c>WHERE</c> clause of the <c>ON CONFLICT DO UPDATE</c> statement when configured for <see cref="BulkInsertMode.OverwriteExisting"/>.
    ///     Use <c>d.</c> to refer to the existing record in the table and <c>excluded.</c> to refer to the conflicting record that was excluded from insertion.
    ///
    ///     <para>
    ///         Example:
    ///         <code>
    ///             "d.export_date &lt;= excluded.export_date"
    ///         </code>
    ///     </para>
    /// </param>
    Task BulkInsertEnlistTransactionAsync<T>(IReadOnlyCollection<T> documents, Transaction transaction,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000,
        string? updateCondition = null, CancellationToken cancellation = default);

    /// <summary>
    ///     Uses Postgresql's COPY ... FROM STDIN BINARY feature to efficiently store
    ///     a large number of documents of type "T" to the database. This operation is transactional.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="tenantId"></param>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    /// <param name="updateCondition">
    ///     Raw sql that is used as the <c>WHERE</c> clause of the <c>ON CONFLICT DO UPDATE</c> statement when configured for <see cref="BulkInsertMode.OverwriteExisting"/>.
    ///     Use <c>d.</c> to refer to the existing record in the table and <c>excluded.</c> to refer to the conflicting record that was excluded from insertion.
    ///
    ///     <para>
    ///         Example:
    ///         <code>
    ///             "d.export_date &lt;= excluded.export_date"
    ///         </code>
    ///     </para>
    /// </param>
    Task BulkInsertAsync<T>(string tenantId, IReadOnlyCollection<T> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly, int batchSize = 1000,
        string? updateCondition = null, CancellationToken cancellation = default);

    /// <summary>
    ///     Open a new IDocumentSession with the supplied DocumentTracking.
    ///     "IdentityOnly" is the default.
    /// </summary>
    /// <param name="tracking"></param>
    /// <returns></returns>
    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    IDocumentSession OpenSession(
        DocumentTracking tracking = DocumentTracking.IdentityOnly,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    );

    /// <summary>
    ///     Open a new IDocumentSession with the supplied DocumentTracking.
    ///     "IdentityOnly" is the default.
    /// </summary>
    /// <param name="tracking"></param>
    /// <returns></returns>
    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    IDocumentSession OpenSession(
        string tenantId,
        DocumentTracking tracking = DocumentTracking.IdentityOnly,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    );

    /// <summary>
    ///     Open a new IDocumentSession with the supplied options
    /// </summary>
    /// <param name="options">Additional options for session</param>
    /// <returns></returns>
    IDocumentSession OpenSession(SessionOptions options);

    /// <summary>
    ///     Open a new IDocumentSession with the supplied options and immediately open
    ///     the database connection and start the transaction for the session. This is approapriate
    ///     for Serializable transaction sessions
    /// </summary>
    /// <param name="options"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    Task<IDocumentSession> OpenSerializableSessionAsync(SessionOptions options, CancellationToken token = default);

    /// <summary>
    ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
    ///     or automatic dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

    /// <summary>
    ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
    ///     or automatic dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession LightweightSession(
        string tenantId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    );

    /// <summary>
    ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
    ///     or automatic dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession LightweightSession(SessionOptions options);

    /// <summary>
    ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
    ///     or automatic dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> LightweightSerializableSessionAsync(CancellationToken token = default);

    /// <summary>
    ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
    ///     or automatic dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> LightweightSerializableSessionAsync(
        string tenantId,
        CancellationToken token = default
    );

    /// <summary>
    ///     Convenience method to create a new "lightweight" IDocumentSession with no IdentityMap
    ///     or automatic dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> LightweightSerializableSessionAsync(SessionOptions options, CancellationToken token = default);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with IdentityMap but without automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession IdentitySession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with IdentityMap but without automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession IdentitySession(
        string tenantId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    );

    /// <summary>
    ///     Convenience method to create an IDocumentSession with IdentityMap but without automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession IdentitySession(SessionOptions options);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with IdentityMap but without automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> IdentitySerializableSessionAsync(CancellationToken token = default);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with IdentityMap but without automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> IdentitySerializableSessionAsync(
        string tenantId,
        CancellationToken token = default
    );

    /// <summary>
    ///     Convenience method to create an IDocumentSession with IdentityMap but without automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> IdentitySerializableSessionAsync(SessionOptions options, CancellationToken token = default);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession DirtyTrackedSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession DirtyTrackedSession(
        string tenantId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    IDocumentSession DirtyTrackedSession(SessionOptions options);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> DirtyTrackedSerializableSessionAsync(CancellationToken token = default);

    /// <summary>
    ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> DirtyTrackedSerializableSessionAsync(
        string tenantId,
        CancellationToken token = default
    );

    /// <summary>
    ///     Convenience method to create an IDocumentSession with both IdentityMap and automatic
    ///     dirty checking
    /// </summary>
    /// <returns></returns>
    Task<IDocumentSession> DirtyTrackedSerializableSessionAsync(SessionOptions options, CancellationToken token = default);

    /// <summary>
    ///     Opens a read-only IQuerySession to the current document store for efficient
    ///     querying without any underlying object tracking.
    /// </summary>
    /// <returns></returns>
    IQuerySession QuerySession();

    /// <summary>
    ///     Opens a read-only IQuerySession to the current document store for efficient
    ///     querying without any underlying object tracking.
    /// </summary>
    /// <returns></returns>
    IQuerySession QuerySession(string tenantId);

    /// <summary>
    ///     Opens a read-only IQuerySession to the current document store for efficient
    ///     querying without any underlying object tracking.
    /// </summary>
    /// <param name="options">Additional options for session. DocumentTracking is not applicable for IQuerySession.</param>
    /// <returns></returns>
    IQuerySession QuerySession(SessionOptions options);

    /// <summary>
    ///     Opens a read-only IQuerySession to the current document store for efficient
    ///     querying without any underlying object tracking.
    /// </summary>
    /// <returns></returns>
    Task<IQuerySession> QuerySerializableSessionAsync(CancellationToken token = default);

    /// <summary>
    ///     Opens a read-only IQuerySession to the current document store for efficient
    ///     querying without any underlying object tracking.
    /// </summary>
    /// <returns></returns>
    Task<IQuerySession> QuerySerializableSessionAsync(string tenantId, CancellationToken token = default);

    /// <summary>
    ///     Opens a read-only IQuerySession to the current document store for efficient
    ///     querying without any underlying object tracking.
    /// </summary>
    /// <param name="options">Additional options for session. DocumentTracking is not applicable for IQuerySession.</param>
    /// <returns></returns>
    Task<IQuerySession> QuerySerializableSessionAsync(SessionOptions options, CancellationToken token = default);

    /// <summary>
    ///     Bulk insert a potentially mixed enumerable of document types
    /// </summary>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000);

    /// <summary>
    ///     Bulk insert a potentially mixed enumerable of document types
    /// </summary>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    void BulkInsertDocuments(string tenantId, IEnumerable<object> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000);

    /// <summary>
    ///     Bulk insert a potentially mixed enumerable of document types
    /// </summary>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    Task BulkInsertDocumentsAsync(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, CancellationToken cancellation = default);

    /// <summary>
    ///     Bulk insert a potentially mixed enumerable of document types
    /// </summary>
    /// <param name="documents"></param>
    /// <param name="mode"></param>
    /// <param name="batchSize"></param>
    Task BulkInsertDocumentsAsync(string tenantId, IEnumerable<object> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, CancellationToken cancellation = default);

    /// <summary>
    ///     Build a new instance of the asynchronous projection daemon to use interactively
    ///     in your own code
    /// </summary>
    /// <param name="tenantIdOrDatabaseIdentifier">
    ///     If using multi-tenancy with multiple databases, supplying this will choose
    ///     the database to target
    /// </param>
    /// <param name="logger">Override the logger inside this instance of the async daemon</param>
    /// <returns></returns>
    ValueTask<IProjectionDaemon> BuildProjectionDaemonAsync(string? tenantIdOrDatabaseIdentifier = null,
        ILogger? logger = null);
}
