using System.Linq;
using System.Threading.Tasks;
using Marten.PLv8.Patching;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.PLv8.Testing.Patching;

public class Bug_593_patch_doc_function_should_be_built_in_designated_schema: BugIntegrationContext
{
    [Fact]
    public async Task should_stick_the_patch_doc_function_in_the_right_schema()
    {
        StoreOptions(_ =>
        {
            _.DatabaseSchemaName = "other";
            _.UseJavascriptTransformsAndPatching();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var expected = new PostgresqlObjectName("other", "mt_transform_patch_doc");
        (await theStore.Tenancy.Default.Database.Functions()).Contains(expected).ShouldBeTrue();
    }
}
