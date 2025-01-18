#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Daemon.HighWater;
using Marten.Events.Daemon.Internals;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal.Sessions;
using Marten.Services;
using Marten.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weasel.Postgresql.Connections;
using IsolationLevel = System.Data.IsolationLevel;

namespace Marten;

/// <summary>
///     The main entry way to using Marten
/// </summary>
public partial class DocumentStore: IDocumentStore, IAsyncDisposable
{
    private readonly IMartenLogger _logger;
    private readonly INpgsqlDataSourceFactory dataSourceFactory;

    /// <summary>
    ///     Creates a new DocumentStore with the supplied StoreOptions
    /// </summary>
    /// <param name="options"></param>
    public DocumentStore(StoreOptions options)
    {
        dataSourceFactory = options.NpgsqlDataSourceFactory;
        options.ApplyConfiguration();
        options.Validate();

        Options = options;
        _logger = options.Logger();
        Serializer = options.Serializer();

        // Workaround to make database creation lazy so all StoreOptions
        // customizations can be done first
        if (Tenancy is DefaultTenancy d)
        {
            d.Initialize();
        }

        StorageFeatures.PostProcessConfiguration();
        Events.Initialize(this);
        Options.Projections.AssertValidity(this);

        Advanced = new AdvancedOperations(this);

        Diagnostics = new Diagnostics(this);

        _lightweightCompiledQueries = new CompiledQueryCollection(DocumentTracking.None, this);
        _identityMapCompiledQueries = new CompiledQueryCollection(DocumentTracking.IdentityOnly, this);
        _dirtyTrackedCompiledQueries = new CompiledQueryCollection(DocumentTracking.DirtyTracking, this);
        _queryOnlyCompiledQueries = new CompiledQueryCollection(DocumentTracking.QueryOnly, this);

        warnIfAsyncDaemonIsDisabledWithAsyncProjections();

        options.ApplyMetricsIfAny();
    }

    private void warnIfAsyncDaemonIsDisabledWithAsyncProjections()
    {
        if (Options.Projections.HasAnyAsyncProjections() && Options.Projections.AsyncMode == DaemonMode.Disabled)
        {
            Console.WriteLine("Warning: The async daemon is disabled.");
            var asyncProjectionList =
                Options.Projections.All.Where(x => x.Lifecycle == ProjectionLifecycle.Async).Select(x => x.ToString())!
                    .Join(", ");
            Console.WriteLine(
                $"Projections {asyncProjectionList} will not be executed without the async daemon enabled");
        }
    }

    public ITenancy Tenancy => Options.Tenancy;

    public EventGraph Events => Options.EventGraph;

    public StorageFeatures StorageFeatures => Options.Storage;

    public ISerializer Serializer { get; }

    public StoreOptions Options { get; }

    IReadOnlyStoreOptions IDocumentStore.Options => Options;

    public virtual void Dispose()
    {
        (dataSourceFactory as IDisposable)?.SafeDispose();
        (Options.Events as IDisposable)?.SafeDispose();
        Tenancy.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        return DisposableExtensions
            .MaybeDisposeAllAsync<object>([dataSourceFactory, Options.Events, Tenancy]);
    }

    public AdvancedOperations Advanced { get; }

    [Obsolete(Marten.Internal.Sessions.QuerySession.SynchronousRemoval)]
    public void BulkInsert<T>(IReadOnlyCollection<T> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, string? updateCondition = null)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
        bulkInsertion.BulkInsert(documents, mode, batchSize, updateCondition);
    }

    [Obsolete(Marten.Internal.Sessions.QuerySession.SynchronousRemoval)]
    public void BulkInsertEnlistTransaction<T>(IReadOnlyCollection<T> documents,
        Transaction transaction, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, string? updateCondition = null)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
        bulkInsertion.BulkInsertEnlistTransaction(documents, transaction, mode, batchSize, updateCondition);
    }

    [Obsolete(Marten.Internal.Sessions.QuerySession.SynchronousRemoval)]
    public void BulkInsertDocuments(IEnumerable<object> documents, BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
        bulkInsertion.BulkInsertDocuments(documents, mode, batchSize);
    }

    [Obsolete(Marten.Internal.Sessions.QuerySession.SynchronousRemoval)]
    public void BulkInsert<T>(string tenantId, IReadOnlyCollection<T> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000,
        string? updateCondition = null)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.GetTenant(Options.MaybeCorrectTenantId(tenantId)), Options);
        bulkInsertion.BulkInsert(documents, mode, batchSize, updateCondition);
    }

    [Obsolete(Marten.Internal.Sessions.QuerySession.SynchronousRemoval)]
    public void BulkInsertDocuments(string tenantId, IEnumerable<object> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.GetTenant(Options.MaybeCorrectTenantId(tenantId)), Options);
        bulkInsertion.BulkInsertDocuments(documents, mode, batchSize);
    }

    public Task BulkInsertAsync<T>(IReadOnlyCollection<T> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000,
        string? updateCondition = null,
        CancellationToken cancellation = default)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
        return bulkInsertion.BulkInsertAsync(documents, mode, batchSize, updateCondition, cancellation);
    }

    public Task BulkInsertEnlistTransactionAsync<T>(IReadOnlyCollection<T> documents,
        Transaction transaction,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000,
        string? updateCondition = null,
        CancellationToken cancellation = default)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
        return bulkInsertion.BulkInsertEnlistTransactionAsync(documents, transaction, mode, batchSize, updateCondition, cancellation);
    }

    public async Task BulkInsertAsync<T>(
        string tenantId,
        IReadOnlyCollection<T> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000,
        string? updateCondition = null,
        CancellationToken cancellation = default)
    {
        var bulkInsertion = new BulkInsertion(await Tenancy.GetTenantAsync(Options.MaybeCorrectTenantId(tenantId)).ConfigureAwait(false), Options);
        await bulkInsertion.BulkInsertAsync(documents, mode, batchSize, updateCondition, cancellation).ConfigureAwait(false);
    }

    public Task BulkInsertDocumentsAsync(IEnumerable<object> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, CancellationToken cancellation = default)
    {
        var bulkInsertion = new BulkInsertion(Tenancy.Default, Options);
        return bulkInsertion.BulkInsertDocumentsAsync(documents, mode, batchSize, cancellation);
    }

    public async Task BulkInsertDocumentsAsync(string tenantId, IEnumerable<object> documents,
        BulkInsertMode mode = BulkInsertMode.InsertsOnly,
        int batchSize = 1000, CancellationToken cancellation = default)
    {
        var bulkInsertion = new BulkInsertion(await Tenancy.GetTenantAsync(Options.MaybeCorrectTenantId(tenantId)).ConfigureAwait(false), Options);
        await bulkInsertion.BulkInsertDocumentsAsync(documents, mode, batchSize, cancellation).ConfigureAwait(false);
    }

    public IDiagnostics Diagnostics { get; }

    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    public IDocumentSession OpenSession(SessionOptions options)
    {
        return openSession(options);
    }

    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    public IDocumentSession OpenSession(
        DocumentTracking tracking = DocumentTracking.IdentityOnly,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    ) =>
        openSession(new SessionOptions { Tracking = tracking, IsolationLevel = isolationLevel });

    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    public IDocumentSession OpenSession(
        string tenantId,
        DocumentTracking tracking = DocumentTracking.IdentityOnly,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    ) =>
        openSession(new SessionOptions { Tracking = tracking, IsolationLevel = isolationLevel, TenantId = Options.MaybeCorrectTenantId(tenantId) });

    public IDocumentSession IdentitySession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
        IdentitySession(new SessionOptions { IsolationLevel = isolationLevel });

    public IDocumentSession IdentitySession(
        string tenantId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    ) =>
        IdentitySession(new SessionOptions { IsolationLevel = isolationLevel, TenantId = Options.MaybeCorrectTenantId(tenantId) });

    public IDocumentSession IdentitySession(SessionOptions options)
    {
        options.Tracking = DocumentTracking.IdentityOnly;
        return openSession(options);
    }

    public Task<IDocumentSession> IdentitySerializableSessionAsync(
        CancellationToken cancellation = default
    ) =>
        IdentitySerializableSessionAsync(
            new SessionOptions { IsolationLevel = IsolationLevel.Serializable },
            cancellation
        );

    public Task<IDocumentSession> IdentitySerializableSessionAsync(
        string tenantId,
        CancellationToken cancellation = default
    ) =>
        IdentitySerializableSessionAsync(
            new SessionOptions { IsolationLevel = IsolationLevel.Serializable, TenantId = Options.MaybeCorrectTenantId(tenantId) },
            cancellation
        );

    public Task<IDocumentSession> IdentitySerializableSessionAsync(
        SessionOptions options,
        CancellationToken cancellation = default
    )
    {
        options.IsolationLevel = IsolationLevel.Serializable;
        options.Tracking = DocumentTracking.IdentityOnly;
        return OpenSerializableSessionAsync(options, cancellation);
    }

    public IDocumentSession DirtyTrackedSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
        DirtyTrackedSession(new SessionOptions { IsolationLevel = isolationLevel });

    public IDocumentSession DirtyTrackedSession(
        string tenantId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    ) =>
        DirtyTrackedSession(new SessionOptions { IsolationLevel = isolationLevel, TenantId = Options.MaybeCorrectTenantId(tenantId) });

    public IDocumentSession DirtyTrackedSession(SessionOptions options)
    {
        options.Tracking = DocumentTracking.DirtyTracking;
        return openSession(options);
    }

    public Task<IDocumentSession> DirtyTrackedSerializableSessionAsync(
        CancellationToken cancellation = default
    ) =>
        DirtyTrackedSerializableSessionAsync(new SessionOptions { IsolationLevel = IsolationLevel.Serializable },
            cancellation);

    public Task<IDocumentSession> DirtyTrackedSerializableSessionAsync(
        string tenantId,
        CancellationToken cancellation = default
    ) =>
        DirtyTrackedSerializableSessionAsync(
            new SessionOptions { IsolationLevel = IsolationLevel.Serializable, TenantId = Options.MaybeCorrectTenantId(tenantId) }, cancellation);

    public Task<IDocumentSession> DirtyTrackedSerializableSessionAsync(
        SessionOptions options,
        CancellationToken cancellation = default
    )
    {
        options.IsolationLevel = IsolationLevel.Serializable;
        options.Tracking = DocumentTracking.DirtyTracking;
        return OpenSerializableSessionAsync(options, cancellation);
    }

    public IDocumentSession LightweightSession(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted) =>
        LightweightSession(new SessionOptions { IsolationLevel = isolationLevel });

    public IDocumentSession LightweightSession(
        string tenantId,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted
    ) =>
        LightweightSession(new SessionOptions { IsolationLevel = isolationLevel, TenantId = Options.MaybeCorrectTenantId(tenantId) });

    public IDocumentSession LightweightSession(SessionOptions options)
    {
        options.Tracking = DocumentTracking.None;
        return openSession(options);
    }

    public Task<IDocumentSession> LightweightSerializableSessionAsync(
        CancellationToken cancellation = default
    ) =>
        LightweightSerializableSessionAsync(new SessionOptions { IsolationLevel = IsolationLevel.Serializable },
            cancellation);

    public Task<IDocumentSession> LightweightSerializableSessionAsync(
        string tenantId,
        CancellationToken cancellation = default
    ) =>
        LightweightSerializableSessionAsync(
            new SessionOptions { IsolationLevel = IsolationLevel.Serializable, TenantId = Options.MaybeCorrectTenantId(tenantId) }, cancellation);

    public Task<IDocumentSession> LightweightSerializableSessionAsync(
        SessionOptions options,
        CancellationToken cancellation = default
    )
    {
        options.IsolationLevel = IsolationLevel.Serializable;
        options.Tracking = DocumentTracking.None;
        return OpenSerializableSessionAsync(options, cancellation);
    }

    public IQuerySession QuerySession(SessionOptions options)
    {
        var connection = options.Initialize(this, CommandRunnerMode.ReadOnly, Options.OpenTelemetry);

        return new QuerySession(this, options, connection);
    }

    public IQuerySession QuerySession() =>
        QuerySession(Marten.Storage.Tenancy.DefaultTenantId);

    public IQuerySession QuerySession(string tenantId) =>
        QuerySession(new SessionOptions { TenantId = Options.MaybeCorrectTenantId(tenantId) });

    public async Task<IQuerySession> QuerySerializableSessionAsync(
        SessionOptions options,
        CancellationToken cancellation = default
    )
    {
        var connection = await options.InitializeAsync(this, CommandRunnerMode.ReadOnly, cancellation)
            .ConfigureAwait(false);

        return new QuerySession(this, options, connection);
    }

    public Task<IQuerySession> QuerySerializableSessionAsync(CancellationToken cancellation = default) =>
        QuerySerializableSessionAsync(Marten.Storage.Tenancy.DefaultTenantId, cancellation);

    public Task<IQuerySession> QuerySerializableSessionAsync(
        string tenantId,
        CancellationToken cancellation = default
    ) =>
        QuerySerializableSessionAsync(
            new SessionOptions { TenantId = Options.MaybeCorrectTenantId(tenantId), IsolationLevel = IsolationLevel.Serializable }, cancellation);

    public IProjectionDaemon BuildProjectionDaemon(
        string? tenantIdOrDatabaseIdentifier = null,
        ILogger? logger = null
    )
    {
        if (tenantIdOrDatabaseIdentifier.IsNotEmpty())
        {
            tenantIdOrDatabaseIdentifier = Options.MaybeCorrectTenantId(tenantIdOrDatabaseIdentifier);
        }

        AssertTenantOrDatabaseIdentifierIsValid(tenantIdOrDatabaseIdentifier);

        logger ??= new NulloLogger();

        var database = tenantIdOrDatabaseIdentifier.IsEmpty()
            ? Tenancy.Default.Database
            : Tenancy.GetTenant(tenantIdOrDatabaseIdentifier).Database;

        var detector = new HighWaterDetector((MartenDatabase)database, Events, logger);

        return new ProjectionDaemon(this, (MartenDatabase)database, logger, detector, new AgentFactory(this));
    }

    public async ValueTask<IProjectionDaemon> BuildProjectionDaemonAsync(
        string? tenantIdOrDatabaseIdentifier = null,
        ILogger? logger = null
    )
    {
        if (tenantIdOrDatabaseIdentifier.IsNotEmpty())
        {
            tenantIdOrDatabaseIdentifier = Options.MaybeCorrectTenantId(tenantIdOrDatabaseIdentifier);
        }

        AssertTenantOrDatabaseIdentifierIsValid(tenantIdOrDatabaseIdentifier);

        logger ??= Options.LogFactory?.CreateLogger<ProjectionDaemon>() ?? Options.DotNetLogger ?? NullLogger.Instance;

        var database = tenantIdOrDatabaseIdentifier.IsEmpty()
            ? Tenancy.Default.Database
            : await Tenancy.FindOrCreateDatabase(tenantIdOrDatabaseIdentifier).ConfigureAwait(false);

        await database.EnsureStorageExistsAsync(typeof(IEvent)).ConfigureAwait(false);

        return database.As<MartenDatabase>().StartProjectionDaemon(this, logger);
    }

    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    public async Task<IDocumentSession> OpenSerializableSessionAsync(SessionOptions options,
        CancellationToken token = default)
    {
        var connection = await options.InitializeAsync(this, CommandRunnerMode.Transactional, token)
            .ConfigureAwait(false);

        IDocumentSession session = options.Tracking switch
        {
            DocumentTracking.None => new LightweightSession(this, options, connection),
            DocumentTracking.IdentityOnly => new IdentityMapDocumentSession(this, options, connection),
            DocumentTracking.DirtyTracking => new DirtyCheckingDocumentSession(this, options, connection),
            _ => throw new ArgumentOutOfRangeException(nameof(SessionOptions.Tracking))
        };

        return session;
    }

    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    public Task<IDocumentSession> OpenSessionAsync(
        DocumentTracking tracking = DocumentTracking.IdentityOnly,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken token = default
    ) =>
        OpenSerializableSessionAsync(new SessionOptions { Tracking = tracking, IsolationLevel = isolationLevel },
            token);

    [Obsolete(
        """
        Opening a session without explicitly providing desired type may be dropped in next Marten version.
        Use explicit method like `LightweightSession`, `IdentitySession` or `DirtyTrackedSession`.
        We recommend using lightweight session by default. Read more in documentation: https://martendb.io/documents/sessions.html.
        """
    )]
    public Task<IDocumentSession> OpenSessionAsync(
        string tenantId,
        DocumentTracking tracking = DocumentTracking.IdentityOnly,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken token = default
    ) =>
        OpenSerializableSessionAsync(
            new SessionOptions { Tracking = tracking, IsolationLevel = isolationLevel, TenantId = tenantId }, token);

    /// <summary>
    ///     Quick way to stand up a DocumentStore to the given database connection
    ///     in the "development" mode for auto-creating schema objects as needed
    ///     with the default behaviors
    /// </summary>
    /// <param name="connectionString"></param>
    /// <returns></returns>
    public static DocumentStore For(string connectionString)
    {
        return For(_ =>
        {
            _.Connection(connectionString);
        });
    }

    /// <summary>
    ///     Configures a DocumentStore for an existing StoreOptions type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static DocumentStore For<T>() where T : StoreOptions, new()
    {
        return new DocumentStore(new T());
    }

    /// <summary>
    ///     Configures a DocumentStore by defining the StoreOptions settings first
    /// </summary>
    /// <param name="configure"></param>
    /// <returns></returns>

    #region sample_DocumentStore.For

    public static DocumentStore For(Action<StoreOptions> configure)
    {
        var options = new StoreOptions();
        configure(options);

        return new DocumentStore(options);
    }

    #endregion

    private IDocumentSession openSession(SessionOptions options)
    {
        var connection = options.Initialize(this, CommandRunnerMode.Transactional, Options.OpenTelemetry);

        IDocumentSession session = options.Tracking switch
        {
            DocumentTracking.None => new LightweightSession(this, options, connection),
            DocumentTracking.IdentityOnly => new IdentityMapDocumentSession(this, options, connection),
            DocumentTracking.DirtyTracking => new DirtyCheckingDocumentSession(this, options, connection),
            _ => throw new ArgumentOutOfRangeException(nameof(SessionOptions.Tracking))
        };

        return session;
    }

    private void AssertTenantOrDatabaseIdentifierIsValid(string? tenantIdOrDatabaseIdentifier)
    {
        if (!Options.Advanced.DefaultTenantUsageEnabled
            && Tenancy is not DefaultTenancy
            && tenantIdOrDatabaseIdentifier.IsEmpty())
        {
            throw new DefaultTenantUsageDisabledException();
        }
    }
}
