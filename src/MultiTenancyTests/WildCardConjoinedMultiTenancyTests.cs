using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Storage;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;

namespace MultiTenancyTests;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; }
}

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class When_using_wildcard_conjoined_multi_tenancy: IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore _store;
    private Guid _id;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(
                services => services.AddMarten(
                    _ =>
                    {
                        _.Connection(ConnectionSource.ConnectionString);
                        _.Tenancy = new WildcardConjoinedMultiTenancy(
                            _,
                            ConnectionSource.ConnectionString,
                            "tenants",
                            "shared"
                        );
                        _.AutoCreateSchemaObjects = AutoCreate.All;
                    }
                )
            )
            .StartAsync();
        _store = _host.Services.GetService<IDocumentStore>();
        _id = Guid.NewGuid();
        var user = new User() { Id = _id, Username = "Jane" };
        await using var session = _store.LightweightSession("shared-green");
        session.Insert(user);
        await session.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_persist_document_for_tenant_id()
    {
        await using var session = _store.LightweightSession("shared-green");
        var user = session.Load<User>(_id);
        user.ShouldNotBeNull();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class When_using_wildcard_conjoined_multi_tenancy_for_two_tenants: IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore _store;
    private Guid _tenant1UserId;
    private Guid _tenant2UserId;
    private string _janeGreen;
    private string _janeRed;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(
                services => services.AddMarten(
                    _ =>
                    {
                        _.Connection(ConnectionSource.ConnectionString);
                        _.Tenancy = new WildcardConjoinedMultiTenancy(
                            _,
                            ConnectionSource.ConnectionString,
                            "tenants",
                            "shared"
                        );
                        _.AutoCreateSchemaObjects = AutoCreate.All;
                    }
                )
            )
            .StartAsync();
        _store = _host.Services.GetService<IDocumentStore>();
        _tenant1UserId = Guid.NewGuid();
        _tenant2UserId = Guid.NewGuid();
        _janeGreen = "Jane Green";
        _janeRed = "Jane Red";
        var tenant1User = new User { Id = _tenant1UserId, Username = _janeGreen };
        var tenant2User = new User { Id = _tenant2UserId, Username = _janeRed };
        await using var tenant1Session = _store.LightweightSession("shared-green");
        tenant1Session.Insert(tenant1User);
        await tenant1Session.SaveChangesAsync();
        await using var tenant2Session = _store.LightweightSession("shared-red");
        tenant2Session.Insert(tenant2User);
        await tenant2Session.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_load_document_for_tenant1()
    {
        await using var session1 = _store.LightweightSession("shared-green");
        var janeGreen = session1.Load<User>(_tenant1UserId);
        janeGreen.Username.ShouldBe(_janeGreen);
    }

    [Fact]
    public async Task Should_load_document_for_tenant2()
    {
        await using var session2 = _store.LightweightSession("shared-red");
        var janeRed = session2.Load<User>(_tenant2UserId);
        janeRed.Username.ShouldBe(_janeRed);
    }

    [Fact]
    public async Task Should_not_load_tenant1_document_for_tenant2_session()
    {
        await using var session2 = _store.LightweightSession("shared-red");
        var janeGreen = session2.Load<User>(_tenant1UserId);
        janeGreen.ShouldBeNull();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class When_using_wildcard_conjoined_multi_tenancy_when_session_id_doesnt_match_wildcard: IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore _store;
    private Guid _id;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(
                services => services.AddMarten(
                    _ =>
                    {
                        _.Connection(ConnectionSource.ConnectionString);
                        _.Tenancy = new WildcardConjoinedMultiTenancy(
                            _,
                            ConnectionSource.ConnectionString,
                            "tenants",
                            "shared"
                        );
                        _.AutoCreateSchemaObjects = AutoCreate.All;
                    }
                )
            )
            .StartAsync();
        _store = _host.Services.GetService<IDocumentStore>();
        _id = Guid.NewGuid();
    }

    [Fact]
    public async Task Should_throw_argument_null_exception_for_tenant()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            async () =>
            {
                var user = new User() { Id = _id, Username = "Jane" };
                await using var session = _store.LightweightSession("green");
                session.Insert(user);
                await session.SaveChangesAsync();
            }
        );
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("multi-tenancy", DisableParallelization = true)]
public class When_using_wildcard_conjoined_multi_tenancy_and_cleaning_up_all_marten_schema_objects: IAsyncLifetime
{
    private IHost _host;
    private IDocumentStore _store;
    private Guid _id;

    public async Task InitializeAsync()
    {
        _host = await Host.CreateDefaultBuilder()
            .ConfigureServices(
                services => services.AddMarten(
                    _ =>
                    {
                        _.Connection(ConnectionSource.ConnectionString);
                        _.Tenancy = new WildcardConjoinedMultiTenancy(
                            _,
                            ConnectionSource.ConnectionString,
                            "tenants",
                            "shared"
                        );
                        _.AutoCreateSchemaObjects = AutoCreate.All;
                    }
                )
            )
            .StartAsync();
        _store = _host.Services.GetService<IDocumentStore>();
        _id = Guid.NewGuid();

        var tenant1User = new User { Id = Guid.NewGuid(), Username = "Jane" };

        await using var tenant1Session = _store.LightweightSession("shared-green");
        tenant1Session.Insert(tenant1User);
        await tenant1Session.SaveChangesAsync();
    }

    [Fact]
    public async Task Should_remove_user_table()
    {
        await _store.Advanced.Clean.CompletelyRemoveAllAsync();
        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();


        var tables = await conn.ExistingTablesAsync();
        tables.Any(x => x.QualifiedName == "public.mt_doc_user").ShouldBeFalse();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }
}
