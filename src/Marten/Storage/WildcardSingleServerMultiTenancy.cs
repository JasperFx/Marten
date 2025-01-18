using System.Collections.Generic;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Schema;
using Npgsql;
using Weasel.Core.Migrations;

namespace Marten.Storage;

public class WildcardConjoinedMultiTenancy: ITenancy
{
    private readonly MartenDatabase _database;
    private readonly string _prefix;
    private ImHashMap<string, Tenant> _tenants = ImHashMap<string, Tenant>.Empty;

    public WildcardConjoinedMultiTenancy(
        StoreOptions options,
        string connectionString,
        string identifier,
        string prefix
    )
    {
        options.Policies.AllDocumentsAreMultiTenanted();
        _database = new MartenDatabase(
            options,
            NpgsqlDataSource.Create(connectionString),
            identifier
        );
        _prefix = prefix;
        Cleaner = new CompositeDocumentCleaner(this, options);
    }

    public ValueTask<IReadOnlyList<IDatabase>> BuildDatabases()
    {
        return new ValueTask<IReadOnlyList<IDatabase>>(new[] { _database });
    }

    public Tenant Default { get; }
    public IDocumentCleaner Cleaner { get; }

    public Tenant GetTenant(
        string tenantId
    )
    {
        if (!tenantId.StartsWith(_prefix)) return null;
        var tenant = new Tenant(tenantId, _database);
        _tenants = _tenants.AddOrUpdate(tenantId, tenant);
        return tenant;
    }

    public ValueTask<Tenant> GetTenantAsync(
        string tenantId
    )
    {
        if (!tenantId.StartsWith(_prefix)) return new ValueTask<Tenant>();

        var tenant = new Tenant(tenantId, _database);
        _tenants = _tenants.AddOrUpdate(tenantId, tenant);
        return ValueTask.FromResult(tenant);
    }

    public async ValueTask<IMartenDatabase> FindOrCreateDatabase(
        string tenantIdOrDatabaseIdentifier
    )
    {
        var tenant = await GetTenantAsync(tenantIdOrDatabaseIdentifier)
            .ConfigureAwait(false);
        return tenant.Database;
    }

    public bool IsTenantStoredInCurrentDatabase(
        IMartenDatabase database,
        string tenantId
    )
    {
        var tenant = GetTenant(tenantId);
        return ReferenceEquals(database, tenant.Database);
    }

    public void Dispose()
    {
        _database.Dispose();
    }
}
