using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;
using Marten.Linq.MatchesSql;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace DocumentDbTests.Reading;

public class query_by_sql: IntegrationContext
{
    private readonly ITestOutputHelper _output;

    public query_by_sql(DefaultStoreFixture fixture, ITestOutputHelper output): base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task can_query_by_document_type()
    {
        var user = new User();
        var company = new Company { Name = "Megadodo Publications" };
        theSession.Store(user);
        theSession.Store(company);
        await theSession.SaveChangesAsync();

        await using var session = theStore.LightweightSession();

        #region sample_sample-query-type-parameter-overload

        dynamic userFromDb = session.Query(user.GetType(), "where id = ?", user.Id).First();
        dynamic companyFromDb =
            (await session.QueryAsync(typeof(Company), "where id = ?", CancellationToken.None, company.Id)).First();

        #endregion

        Assert.Equal(user.Id, userFromDb.Id);
        Assert.Equal(company.Name, companyFromDb.Name);
    }

    [Fact]
    public async Task stream_query_by_one_parameter()
    {
        await using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();

        var stream = new MemoryStream();
        await session.StreamJson<User>(stream, "where data ->> 'LastName' = ?", "Miller");

        stream.Position = 0;
        var results = theStore.Options.Serializer().FromJson<User[]>(stream);
        var firstnames = results
            .OrderBy(x => x.FirstName)
            .Select(x => x.FirstName).ToArray();

        firstnames.Length.ShouldBe(3);
        firstnames[0].ShouldBe("Jeremy");
        firstnames[1].ShouldBe("Lindsey");
        firstnames[2].ShouldBe("Max");
    }

    [Fact]
    public async Task query_by_one_parameter()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();

        var firstnames =
            session.Query<User>("where data ->> 'LastName' = ?", "Miller").OrderBy(x => x.FirstName)
                .Select(x => x.FirstName).ToArray();

        firstnames.Length.ShouldBe(3);
        firstnames[0].ShouldBe("Jeremy");
        firstnames[1].ShouldBe("Lindsey");
        firstnames[2].ShouldBe("Max");
    }

    [Fact]
    public async Task query_ignores_case_of_where_keyword()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();

        var firstnames =
            session.Query<User>("WHERE data ->> 'LastName' = ?", "Miller").OrderBy(x => x.FirstName)
                .Select(x => x.FirstName).ToArray();

        firstnames.Length.ShouldBe(3);
        firstnames[0].ShouldBe("Jeremy");
        firstnames[1].ShouldBe("Lindsey");
        firstnames[2].ShouldBe("Max");
    }

    [Fact]
    public async Task query_by_one_named_parameter()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();

        var firstnames =
            session.Query<User>("where data ->> 'LastName' = :Name", new { Name = "Miller" })
                .OrderBy(x => x.FirstName)
                .Select(x => x.FirstName).ToArray();

        firstnames.Length.ShouldBe(3);
        firstnames[0].ShouldBe("Jeremy");
        firstnames[1].ShouldBe("Lindsey");
        firstnames[2].ShouldBe("Max");
    }

    [Fact]
    public async Task query_by_two_parameters()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();

        #region sample_using_parameterized_sql

        var user =
            session.Query<User>("where data ->> 'FirstName' = ? and data ->> 'LastName' = ?", "Jeremy",
                    "Miller")
                .Single();

        #endregion

        user.ShouldNotBeNull();
    }

    #region sample_query_by_two_named_parameters

    [Fact]
    public async Task query_by_two_named_parameters()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();
        var user =
            session.Query<User>("where data ->> 'FirstName' = :FirstName and data ->> 'LastName' = :LastName",
                    new { FirstName = "Jeremy", LastName = "Miller" })
                .Single();

        user.ShouldNotBeNull();
    }

    #endregion

    [Fact]
    public async Task query_two_fields_by_one_named_parameter()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();
        var user =
            session.Query<User>("where data ->> 'FirstName' = :Name or data ->> 'LastName' = :Name",
                    new { Name = "Jeremy" })
                .Single();

        user.ShouldNotBeNull();
    }

    [Fact]
    public async Task query_for_multiple_documents()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();

        var firstnames =
            session.Query<User>("where data ->> 'LastName' = 'Miller'").OrderBy(x => x.FirstName)
                .Select(x => x.FirstName).ToArray();

        firstnames.Length.ShouldBe(3);
        firstnames[0].ShouldBe("Jeremy");
        firstnames[1].ShouldBe("Lindsey");
        firstnames[2].ShouldBe("Max");
    }


    [Fact]
    public async Task query_for_multiple_documents_with_ordering()
    {
        using var session = theStore.LightweightSession();
        session.Store(new User { FirstName = "Jeremy", LastName = "Miller" });
        session.Store(new User { FirstName = "Lindsey", LastName = "Miller" });
        session.Store(new User { FirstName = "Max", LastName = "Miller" });
        session.Store(new User { FirstName = "Frank", LastName = "Zombo" });
        await session.SaveChangesAsync();

        var firstnames =
            session.Query<User>("where data ->> 'LastName' = 'Miller' order by data ->> 'FirstName'")
                .Select(x => x.FirstName).ToArray();

        firstnames.Length.ShouldBe(3);
        firstnames[0].ShouldBe("Jeremy");
        firstnames[1].ShouldBe("Lindsey");
        firstnames[2].ShouldBe("Max");
    }


    #region sample_query_with_only_the_where_clause

    [Fact]
    public async Task query_for_single_document()
    {
        using var session = theStore.LightweightSession();
        var u = new User { FirstName = "Jeremy", LastName = "Miller" };
        session.Store(u);
        await session.SaveChangesAsync();

        var user = session.Query<User>("where data ->> 'FirstName' = 'Jeremy'").Single();
        user.LastName.ShouldBe("Miller");
        user.Id.ShouldBe(u.Id);
    }

    #endregion

    [Fact]
    public async Task query_for_single_document_where_clause_trimmed()
    {
        using var session = theStore.LightweightSession();
        var u = new User { FirstName = "Jeremy", LastName = "Miller" };
        session.Store(u);
        await session.SaveChangesAsync();

        var user = session.Query<User>(@"
where data ->> 'FirstName' = 'Jeremy'").Single();
        user.LastName.ShouldBe("Miller");
        user.Id.ShouldBe(u.Id);
    }

    #region sample_query_with_matches_sql

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

    #endregion

    [Fact]
    public async Task query_with_select_in_query()
    {
        using var session = theStore.LightweightSession();
        var u = new User { FirstName = "Jeremy", LastName = "Miller" };
        session.Store(u);
        await session.SaveChangesAsync();

        #region sample_use_all_your_own_sql

        var user =
            session.Query<User>("select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy'")
                .Single();

        #endregion

        user.LastName.ShouldBe("Miller");
        user.Id.ShouldBe(u.Id);
    }

    [Fact]
    public async Task query_with_select_in_query_async()
    {
        await using var session = theStore.LightweightSession();
        var time = DateTimeOffset.UtcNow;
        var u = new User { FirstName = "Jeremy", LastName = "Miller", ModifiedAt = time, Age = 28 };
        session.Store(u);
        await session.SaveChangesAsync();

        #region sample_using-queryasync

        var users =
            await
                session.QueryAsync<User>(
                    "select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy'");
        var user = users.Single();

        #endregion

        user.LastName.ShouldBe("Miller");
        user.Id.ShouldBe(u.Id);

        var ids = await session.QueryAsync<Guid>("SELECT id from mt_doc_user");
        var id = ids.Single();
        id.ShouldBe(u.Id);

        #region sample_using-queryasync-casting

        var times = await session.QueryAsync<DateTimeOffset>(
            "SELECT (data ->> 'ModifiedAt')::timestamptz from mt_doc_user");

        #endregion

        var fetchedTime = times.Single();
        fetchedTime.Second.ShouldBe(time.Second);

        var firstNames = await session.QueryAsync<string>("SELECT (data ->> 'FirstName') from mt_doc_user");
        var firstName = firstNames.Single();
        firstName.ShouldBe(u.FirstName);

        var ages = await session.QueryAsync<int>("SELECT (data ->> 'Age')::integer from mt_doc_user");
        var age = ages.Single();
        age.ShouldBe(u.Age);
    }

    [Fact]
    public async Task get_sum_of_integers_asynchronously()
    {
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 2 });
        theSession.Store(new Target { Color = Colors.Green, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();
        var sumResults =
            await theSession.QueryAsync<int>(
                "select sum(CAST(d.data ->> 'Number' as integer)) as number from mt_doc_target as d");
        var sum = sumResults.Single();
        sum.ShouldBe(10);
    }

    [Fact]
    public async Task get_count_asynchronously()
    {
        var session = theSession;
        theSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        theSession.Store(new Target { Color = Colors.Red, Number = 2 });
        theSession.Store(new Target { Color = Colors.Green, Number = 3 });
        theSession.Store(new Target { Color = Colors.Blue, Number = 4 });

        await theSession.SaveChangesAsync();

        #region sample_query_by_full_sql

        var sumResults = await session
            .QueryAsync<int>("select count(*) from mt_doc_target");

        #endregion

        var sum = sumResults.Single();
        sum.ShouldBe(4);
    }

    [Fact]
    public async Task can_query_using_with_select()
    {
        await using var session = theStore.LightweightSession();
        var time = DateTimeOffset.UtcNow;
        var u = new User { FirstName = "Jeremy", LastName = "Miller", ModifiedAt = time, Age = 28 };
        session.Store(u);
        await session.SaveChangesAsync();

        var users =
            await
                session.QueryAsync<User>(
                    "with my_with_query as (select data from mt_doc_user where data ->> 'FirstName' = 'Jeremy') select data from my_with_query");
        var user = users.Single();

        user.LastName.ShouldBe("Miller");
        user.Id.ShouldBe(u.Id);
    }
}
