using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.MatchesSql;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Xunit.Abstractions;

namespace LinqTests.Bugs;

public class Bug_3087_using_JsonPath_with_MatchesSql : BugIntegrationContext
{
    private readonly ITestOutputHelper _output;

    public Bug_3087_using_JsonPath_with_MatchesSql(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task can_use_json_path_operations()
    {
        await theStore.BulkInsertDocumentsAsync(Target.GenerateRandomData(100).ToArray());

        theSession.Logger = new TestOutputMartenLogger(_output);
        var results = await theSession.Query<Target>().Where(x => !x.Children.Any()).ToListAsync();

        #region sample_using_MatchesJsonPath

        var results2 = await theSession
            .Query<Target>().Where(x => x.MatchesSql('^', "d.data @? '$ ? (@.Children[*] == null || @.Children[*].size() == 0)'"))
            .ToListAsync();

        // older approach that only supports the ^ placeholder
        var results3 = await theSession
            .Query<Target>().Where(x => x.MatchesJsonPath("d.data @? '$ ? (@.Children[*] == null || @.Children[*].size() == 0)'"))
            .ToListAsync();

        #endregion
    }
}


