using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Linq.MatchesSql;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql.SqlGeneration;

namespace LinqTests.Acceptance;

public class matches_sql_queries: IntegrationContext
{
    [Fact]
    public async Task query_using_matches_sql()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        await using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        // no where clause
        session.Query<User>().Where(x => x.MatchesSql("d.data ->> 'UserName' = ? or d.data ->> 'UserName' = ?", "baz", "jack")).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("baz", "jack");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "baz" && x.MatchesSql("d.data ->> 'UserName' != ? and d.data ->> 'UserName' != ?", "foo", "bar"))
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .Single().ShouldBe("jack");
    }

    [Fact]
    public async Task query_using_where_fragment()
    {
        var user1 = new User { UserName = "foo" };
        var user2 = new User { UserName = "bar" };
        var user3 = new User { UserName = "baz" };
        var user4 = new User { UserName = "jack" };

        await using var session = theStore.LightweightSession();
        session.Store(user1, user2, user3, user4);
        await session.SaveChangesAsync();

        var whereFragment = CompoundWhereFragment.And();
        whereFragment.Add(new WhereFragment("d.data ->> 'UserName' != ?", "baz"));
        whereFragment.Add(new WhereFragment("d.data ->> 'UserName' != ?", "jack"));

        // no where clause
        session.Query<User>().Where(x => x.MatchesSql(whereFragment)).OrderBy(x => x.UserName).Select(x => x.UserName)
            .ToList().ShouldHaveTheSameElementsAs("bar", "foo");

        // with a where clause
        session.Query<User>().Where(x => x.UserName != "bar" && x.MatchesSql(whereFragment))
            .OrderBy(x => x.UserName)
            .ToList()
            .Select(x => x.UserName)
            .Single().ShouldBe("foo");
    }

    [Fact]
    public void Throws_NotSupportedException_when_called_directly()
    {
        Should.Throw<NotSupportedException>(
            () => new object().MatchesSql("d.data ->> 'UserName' = ? or d.data ->> 'UserName' = ?", "baz", "jack"));
        Should.Throw<NotSupportedException>(
            () => new object().MatchesSql(new WhereFragment("d.data ->> 'UserName' != ?", "baz")));
    }

    public matches_sql_queries(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}
