# Mixing Raw SQL with Linq

Combine your Linq queries with raw SQL using the `MatchesSql(sql)` method like so:

<!-- snippet: sample_query_with_matches_sql -->
<a id='snippet-sample_query_with_matches_sql'></a>
```cs
[Fact]
public async Task query_with_matches_sql()
{
    using var session = theStore.LightweightSession();
    var u = new User { FirstName = "Eric", LastName = "Smith" };
    session.Store(u);
    await session.SaveChangesAsync();

    var user = session.Query<User>().Where(x => x.MatchesSql("data->> 'FirstName' = ?", "Eric")).Single();
    user.LastName.ShouldBe("Smith");
    user.Id.ShouldBe(u.Id);
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/DocumentDbTests/Reading/query_by_sql.cs#L267-L282' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_query_with_matches_sql' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Named parameters are not supported here, and will throw at runtime if they are used.

**But**, if you want to take advantage of the more recent and very powerful JSONPath style querying, use this flavor of 
the same functionality that behaves exactly the same, but uses the '^' character for parameter placeholders to disambiguate
from the '?' character that is widely used in JSONPath expressions:

<!-- snippet: sample_using_MatchesJsonPath -->
<a id='snippet-sample_using_MatchesJsonPath'></a>
```cs
var results2 = await theSession
    .Query<Target>().Where(x => x.MatchesJsonPath("d.data @? '$ ? (@.Children[*] == null || @.Children[*].size() == 0)'"))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_3087_using_JsonPath_with_MatchesSql.cs#L28-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_MatchesJsonPath' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
