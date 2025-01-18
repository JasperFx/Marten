using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Metadata;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Reading;

public class advanced_sql_query: IntegrationContext
{
    public advanced_sql_query(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task can_query_scalar()
    {
        await using var session = theStore.LightweightSession();
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        await session.SaveChangesAsync();
        #region sample_advanced_sql_query_single_scalar
        var schema = session.DocumentStore.Options.Schema;
        var name = (await session.AdvancedSql.QueryAsync<string>(
            $"select data ->> 'Name' from {schema.For<DocWithMeta>()} limit 1",
            CancellationToken.None)).First();
        #endregion
        name.ShouldBe("Max");
    }

    [Fact]
    public async Task can_query_multiple_scalars()
    {
        await using var session = theStore.LightweightSession();
        #region sample_advanced_sql_query_multiple_scalars
        var (number,text, boolean) = (await session.AdvancedSql.QueryAsync<int, string, bool>(
            "select row(5), row('foo'), row(true) from (values(1)) as dummy",
            CancellationToken.None)).First();
        #endregion
        number.ShouldBe(5);
        text.ShouldBe("foo");
        boolean.ShouldBe(true);
    }

    [Fact]
    public async Task can_query_non_document_classes_from_json()
    {
        await using var session = theStore.LightweightSession();
        #region sample_advanced_sql_query_json_object
        var result = (await session.AdvancedSql.QueryAsync<Foo, Bar>(
            "select row(json_build_object('Name', 'foo')), row(json_build_object('Name', 'bar')) from (values(1)) as dummy",
            CancellationToken.None)).First();
        #endregion
        result.Item1.Name.ShouldBe("foo");
        result.Item2.Name.ShouldBe("bar");
    }

    [Fact]
    public async Task can_query_documents()
    {
        await using var session = theStore.LightweightSession();
        session.Store(new DocWithoutMeta { Id = 1, Name = "Max" });
        session.Store(new DocWithoutMeta { Id = 2, Name = "Anne" });
        await session.SaveChangesAsync();
        #region sample_advanced_sql_query_documents
        var schema = session.DocumentStore.Options.Schema;
        var docs = await session.AdvancedSql.QueryAsync<DocWithoutMeta>(
            $"select id, data from {schema.For<DocWithoutMeta>()} order by data ->> 'Name'",
            CancellationToken.None);
        #endregion
        docs.Count.ShouldBe(2);
        docs[0].Name.ShouldBe("Anne");
        docs[1].Name.ShouldBe("Max");
    }

    [Fact]
    public async Task can_query_documents_and_will_set_metadata_on_result_documents()
    {
        await using var session = theStore.LightweightSession();
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        await session.SaveChangesAsync();
        #region sample_advanced_sql_query_documents_with_metadata
        var schema = session.DocumentStore.Options.Schema;
        var doc = (await session.AdvancedSql.QueryAsync<DocWithMeta>(
            $"select id, data, mt_version from {schema.For<DocWithMeta>()} where data ->> 'Name' = 'Max'",
            CancellationToken.None)).First();
        #endregion
        doc.Id.ShouldBe(1);
        doc.Name.ShouldBe("Max");
        doc.Version.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task can_query_multiple_documents_and_scalar()
    {
        await using var session = theStore.LightweightSession();
        #region sample_advanced_sql_query_related_documents_and_scalar
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        session.Store(new DocDetailsWithMeta { Id = 1, Detail = "Likes bees" });
        session.Store(new DocWithMeta { Id = 2, Name = "Michael" });
        session.Store(new DocDetailsWithMeta { Id = 2, Detail = "Is a good chess player" });
        session.Store(new DocWithMeta { Id = 3, Name = "Anne" });
        session.Store(new DocDetailsWithMeta { Id = 3, Detail = "Hates soap operas" });
        session.Store(new DocWithMeta { Id = 4, Name = "Beatrix" });
        session.Store(new DocDetailsWithMeta { Id = 4, Detail = "Likes to cook" });
        await session.SaveChangesAsync();

        var schema = session.DocumentStore.Options.Schema;
        IReadOnlyList<(DocWithMeta doc, DocDetailsWithMeta detail, long totalResults)> results =
            await session.AdvancedSql.QueryAsync<DocWithMeta, DocDetailsWithMeta, long>(
                $"""
                select
                  row(a.id, a.data, a.mt_version),
                  row(b.id, b.data, b.mt_version),
                  row(count(*) over())
                from
                  {schema.For<DocWithMeta>()} a
                left join
                  {schema.For<DocDetailsWithMeta>()} b on a.id = b.id
                where
                  (a.data ->> 'Id')::int > 1
                order by
                  a.data ->> 'Name'
                limit 2
                """,
                CancellationToken.None);

        results.Count.ShouldBe(2);
        results[0].totalResults.ShouldBe(3);
        results[0].doc.Name.ShouldBe("Anne");
        results[0].detail.Detail.ShouldBe("Hates soap operas");
        results[1].doc.Name.ShouldBe("Beatrix");
        results[1].detail.Detail.ShouldBe("Likes to cook");
        #endregion
    }

    [Fact]
    public async Task can_query_with_parameters()
    {
        await using var session = theStore.LightweightSession();
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        await session.SaveChangesAsync();

        #region sample_advanced_sql_query_parameters
        var schema = session.DocumentStore.Options.Schema;

        var name = (await session.AdvancedSql.QueryAsync<string>(
            $"select data ->> ? from {schema.For<DocWithMeta>()} limit 1",
            CancellationToken.None,
            "Name")).First();

        // Use ^ as the parameter placeholder
        var name2 = (await session.AdvancedSql.QueryAsync<string>(
            '^',
            $"select data ->> ^ from {schema.For<DocWithMeta>()} limit 1",
            CancellationToken.None,
            "Name")).First();

        #endregion

        name.ShouldBe("Max");
        name2.ShouldBe("Max");
    }

    [Fact]
    public async Task can_async_stream_multiple_documents_and_scalar()
    {
        await using var session = theStore.LightweightSession();
        #region sample_advanced_sql_stream_related_documents_and_scalar
        session.Store(new DocWithMeta { Id = 1, Name = "Max" });
        session.Store(new DocDetailsWithMeta { Id = 1, Detail = "Likes bees" });
        session.Store(new DocWithMeta { Id = 2, Name = "Michael" });
        session.Store(new DocDetailsWithMeta { Id = 2, Detail = "Is a good chess player" });
        session.Store(new DocWithMeta { Id = 3, Name = "Anne" });
        session.Store(new DocDetailsWithMeta { Id = 3, Detail = "Hates soap operas" });
        session.Store(new DocWithMeta { Id = 4, Name = "Beatrix" });
        session.Store(new DocDetailsWithMeta { Id = 4, Detail = "Likes to cook" });
        await session.SaveChangesAsync();

        var schema = session.DocumentStore.Options.Schema;

        var asyncEnumerable = session.AdvancedSql.StreamAsync<DocWithMeta, DocDetailsWithMeta, long>(
                $"""
                select
                  row(a.id, a.data, a.mt_version),
                  row(b.id, b.data, b.mt_version),
                  row(count(*) over())
                from
                  {schema.For<DocWithMeta>()} a
                left join
                  {schema.For<DocDetailsWithMeta>()} b on a.id = b.id
                where
                  (a.data ->> 'Id')::int > 1
                order by
                  a.data ->> 'Name'
                """,
                CancellationToken.None);

        var collectedResults = new List<(DocWithMeta doc, DocDetailsWithMeta detail, long totalResults)>();
        await foreach (var result in asyncEnumerable)
        {
            collectedResults.Add(result);
        }
        #endregion
        collectedResults.Count.ShouldBe(3);
        collectedResults[0].totalResults.ShouldBe(3);
        collectedResults[0].doc.Name.ShouldBe("Anne");
        collectedResults[0].detail.Detail.ShouldBe("Hates soap operas");
        collectedResults[1].totalResults.ShouldBe(3);
        collectedResults[1].doc.Name.ShouldBe("Beatrix");
        collectedResults[1].detail.Detail.ShouldBe("Likes to cook");
        collectedResults[2].totalResults.ShouldBe(3);
        collectedResults[2].doc.Name.ShouldBe("Michael");
        collectedResults[2].detail.Detail.ShouldBe("Is a good chess player");
    }

    [Fact]
    public void can_query_synchrounously()
    {
        using var session = theStore.LightweightSession();

        var singleResult  = session.AdvancedSql.Query<int>("select 5 from (values(1)) as dummy").First();
        var tuple2Result = session.AdvancedSql.Query<int, string>(
            "select row(5), row('foo')from (values(1)) as dummy").First();
        var tuple3Result = session.AdvancedSql.Query<int, string, bool>(
            "select row(5), row('foo'), row(true) from (values(1)) as dummy").First();

        singleResult.ShouldBe(5);

        tuple2Result.Item1.ShouldBe(5);
        tuple2Result.Item2.ShouldBe("foo");

        tuple3Result.Item1.ShouldBe(5);
        tuple3Result.Item2.ShouldBe("foo");
        tuple3Result.Item3.ShouldBe(true);
    }

    public class DocWithoutMeta
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class DocWithMeta: IVersioned
    {
        public int Id { get; set; }
        public string Name { get; set; }
        [JsonIgnore]
        public Guid Version { get; set; }
    }

    public class DocDetailsWithMeta: IVersioned
    {
        public int Id { get; set; }
        public string Detail { get; set; }
        [JsonIgnore]
        public Guid Version { get; set; }
    }

    public class Foo
    {
        public string Name { get; set; }
    }

    public class Bar
    {
        public string Name { get; set; }
    }
}
