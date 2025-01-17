using System;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Schema.Indexing.Unique;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Xunit;

namespace DocumentDbTests.MultiTenancy;

public class UniqueIndexMultiTenantTests: OneOffConfigurationsContext
{
    public class Project
    {
        public Project()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        public string Name { get; set; }
    }

    public class ProjectUsingDuplicateField: Project
    {
    } //used for duplicatedfield index tests

    //used for attributes index tests
    public class UniqueCodePerTenant
    {
        public UniqueCodePerTenant()
        {
            Id = Guid.NewGuid();
        }

        public Guid Id { get; set; }

        [UniqueIndex(TenancyScope = TenancyScope.PerTenant, IndexType = UniqueIndexType.Computed,
            IndexName = "ScopedPerTenant")]
        public string Code { get; set; }
    }

    [Fact]
    public async Task given_two_documents_for_different_tenants_succeeds_using_attribute()
    {
        var store = StoreOptions(_ =>
        {
            //index definition set on attribute of UniqueCodePerTenant
            _.Schema.For<UniqueCodePerTenant>().MultiTenanted();

            _.Connection(ConnectionSource.ConnectionString);
            _.NameDataLength = 100;
        });

        using var session = store.LightweightSession();
        session.Store(new UniqueCodePerTenant { Code = "ABC" });
        await session.SaveChangesAsync();

        session.Store(new UniqueCodePerTenant { Code = "ABC" });

        try
        {
            await session.SaveChangesAsync();
        }
        catch (DocumentAlreadyExistsException exception)
        {
            ((PostgresException)exception.InnerException)?.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        }
    }

    [Fact]
    public async Task
        given_two_documents_with_the_same_value_for_unique_field_with_single_property_for_different_tenants_succeeds_using_computed_index()
    {
        var store = StoreOptions(_ =>
        {
            _.Schema.For<Project>().MultiTenanted();
            _.Schema.For<Project>()
                .UniqueIndex(UniqueIndexType.Computed, "index_name", TenancyScope.PerTenant,
                    x => x.Name); //have to pass in index name
            _.Connection(ConnectionSource.ConnectionString);
        });

        //default tenant unique constraints still work
        using (var session = store.LightweightSession())
        {
            session.Store(new Project { Name = "Project A" });
            await session.SaveChangesAsync();

            session.Store(new Project { Name = "Project A" });

            try
            {
                await session.SaveChangesAsync();
            }
            catch (DocumentAlreadyExistsException exception)
            {
                ((PostgresException)exception.InnerException).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            }
        }

        //but tenant abc can add a project with the same name
        using (var session = store.LightweightSession("abc"))
        {
            session.Store(new Project { Name = "Project A" });
            await session.SaveChangesAsync();
        }

        //as can tenant def, but only once within the tenant
        using (var session = store.LightweightSession("def"))
        {
            session.Store(new Project { Name = "Project A" });
            await session.SaveChangesAsync();
            session.Store(new Project { Name = "Project A" });

            try
            {
                await session.SaveChangesAsync();
            }
            catch (DocumentAlreadyExistsException exception)
            {
                ((PostgresException)exception.InnerException).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            }
        }
    }

    [Fact]
    public async Task
        given_two_documents_with_the_same_value_for_unique_field_with_single_property_for_different_tenants_succeeds_using_duplicated_field()
    {
        var store = StoreOptions(_ =>
        {
            _.NameDataLength = 200;
            _.Schema.For<ProjectUsingDuplicateField>().MultiTenanted();
            _.Schema.For<ProjectUsingDuplicateField>().DocumentAlias("ProjectUsingDuplicateField");
            _.Schema.For<ProjectUsingDuplicateField>().UniqueIndex(UniqueIndexType.DuplicatedField,
                "ix_duplicate_field", TenancyScope.PerTenant, x => x.Name); //have to pass in index name
            _.Connection(ConnectionSource.ConnectionString);
        });

        //default tenant unique constraints still work
        using (var session = store.LightweightSession())
        {
            session.Store(new ProjectUsingDuplicateField { Name = "Project A" });
            await session.SaveChangesAsync();

            session.Store(new ProjectUsingDuplicateField { Name = "Project A" });

            try
            {
                await session.SaveChangesAsync();
            }
            catch (DocumentAlreadyExistsException exception)
            {
                ((PostgresException)exception.InnerException)?.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            }
        }

        //but tenant abc can add a project with the same name
        using (var session = store.LightweightSession("abc"))
        {
            session.Store(new ProjectUsingDuplicateField { Name = "Project A" });
            await session.SaveChangesAsync();
        }

        //as can tenant def, but only once within the tenant
        using (var session = store.LightweightSession("def"))
        {
            session.Store(new ProjectUsingDuplicateField { Name = "Project A" });
            await session.SaveChangesAsync();
            session.Store(new ProjectUsingDuplicateField { Name = "Project A" });

            try
            {
                await session.SaveChangesAsync();
            }
            catch (DocumentAlreadyExistsException exception)
            {
                ((PostgresException)exception.InnerException).SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            }
        }
    }
}
