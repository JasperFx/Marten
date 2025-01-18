#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Events;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using Polly;

namespace Marten.Internal.Sessions;

public partial class QuerySession: IMartenSession, IQuerySession
{
    public const string SynchronousRemoval =
        "All synchronous APIs that result in database calls will be removed in Marten 8.0. Please use the asynchronous equivalent";

    internal const char DefaultParameterPlaceholder = '?';

    private readonly DocumentStore _store;
    private readonly ResiliencePipeline _resilience;

    internal virtual DocumentTracking TrackingMode => DocumentTracking.QueryOnly;

    public ISerializer Serializer { get; }

    public StoreOptions Options { get; }
    public IQueryEventStore Events { get; }

    protected virtual IQueryEventStore CreateEventStore(DocumentStore store, Tenant tenant)
    {
        return new QueryEventStore(this, store, tenant);
    }

    public IList<IDocumentSessionListener> Listeners { get; } = new List<IDocumentSessionListener>();

    internal SessionOptions SessionOptions { get; }

    /// <summary>
    ///     Used for code generation
    /// </summary>
#nullable disable

    public IMartenDatabase Database { get; protected set; }

    public string TenantId { get; protected set; }
#nullable enable

    internal QuerySession(
        DocumentStore store,
        SessionOptions sessionOptions,
        IConnectionLifetime connection,
        Tenant? tenant = default
    )
    {
        _store = store;
        TenantId = store.Options.MaybeCorrectTenantId(tenant?.TenantId ?? sessionOptions.Tenant?.TenantId ?? sessionOptions.TenantId);
        Database = tenant?.Database ?? sessionOptions.Tenant?.Database ??
            throw new ArgumentNullException(nameof(SessionOptions.Tenant));

        SessionOptions = sessionOptions;

        Listeners.AddRange(store.Options.Listeners);

        if (sessionOptions.Timeout is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sessionOptions.Timeout),
                "CommandTimeout can't be less than zero");
        }

        Listeners.AddRange(sessionOptions.Listeners);
        _providers = sessionOptions.Tenant.Database.Providers ??
                     throw new ArgumentNullException(nameof(IMartenDatabase.Providers));

        _connection = connection;
        Serializer = store.Serializer;
        Options = store.Options;

        Events = CreateEventStore(store, tenant ?? sessionOptions.Tenant);

        Logger = store.Options.Logger().StartSession(this);

        _resilience = Options.ResiliencePipeline;
    }

    public ConcurrencyChecks Concurrency { get; protected set; } = ConcurrencyChecks.Enabled;

    public NpgsqlConnection Connection
    {
        get
        {
            switch (_connection)
            {
                case IAlwaysConnectedLifetime lifetime:
                    return lifetime.Connection;
                case ITransactionStarter starter:
                {
                    var l = starter.Start();
                    _connection = l;
                    return l.Connection;
                }
                default:
                    throw new InvalidOperationException(
                        $"The current lifetime {_connection} is neither a {nameof(IAlwaysConnectedLifetime)} nor a {nameof(ITransactionStarter)}");
            }
        }
    }

    public IMartenSessionLogger Logger
    {
        get => _connection.Logger;
        set => _connection.Logger = value;
    }

    public int RequestCount { get; set; }
    public IDocumentStore DocumentStore => _store;

    public IAdvancedSql AdvancedSql => this;
    public Task<T> QueryByPlanAsync<T>(IQueryPlan<T> plan, CancellationToken token = default)
    {
        // This is literally like this *just* to make mocking easier -- even though I don't agree with that often!
        return plan.Fetch(this, token);
    }
}
