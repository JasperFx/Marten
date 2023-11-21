using System.Threading.Tasks;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit;

namespace CoreTests.Schema;

public class CaseSensitveSchemaGenerationTests: OneOffConfigurationsContext
{
    [Fact]
    public async Task AppliesChanges()
    {
        StoreOptions(options =>
        {
            options.Advanced.UseCaseSensitiveQualifiedNamesWhenApplyingChanges = true;

            options.Schema
                .For<User>()
                .Index(u => u.FirstName, i => i.Name = "cAsEsEnSiTivE");
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        await theStore.Storage.Database.AssertDatabaseMatchesConfigurationAsync();
    }
}
