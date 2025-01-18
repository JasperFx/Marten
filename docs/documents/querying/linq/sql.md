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

**But**, if you want to take advantage of the more recent and very powerful JSONPath style querying, you will find that using `?` as a placeholder is not suitable, as that character is widely used in JSONPath expressions.  If you encounter this issue or write another query where the `?` character is not suitable, you can change the placeholder by providing an alternative. Pass this in before the sql argument.

Older version of Marten also offer the `MatchesJsonPath()` method which uses the `^` character as a placeholder. This will continue to be supported.

<!-- snippet: sample_using_MatchesJsonPath -->
<a id='snippet-sample_using_MatchesJsonPath'></a>
```cs
var results2 = await theSession
    .Query<Target>().Where(x => x.MatchesSql('^', "d.data @? '$ ? (@.Children[*] == null || @.Children[*].size() == 0)'"))
    .ToListAsync();

// older approach that only supports the ^ placeholder
var results3 = await theSession
    .Query<Target>().Where(x => x.MatchesJsonPath("d.data @? '$ ? (@.Children[*] == null || @.Children[*].size() == 0)'"))
    .ToListAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/LinqTests/Bugs/Bug_3087_using_JsonPath_with_MatchesSql.cs#L28-L39' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_using_MatchesJsonPath' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
