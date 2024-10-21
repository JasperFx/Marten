using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_1325_Any_with_contains_on_IList_of_string: BugIntegrationContext
{
    [Fact]
    public async Task can_do_any_with_contains_against_IList()
    {
        var doc1 = new DocWithLists { Names = new List<string> { "Jeremy", "Josh", "Corey" } };
        var doc2 = new DocWithLists { Names = new List<string> { "Jeremy", "Lindsey", "Max" } };
        var doc3 = new DocWithLists { Names = new List<string> { "Jack", "Lindsey", "Max" } };

        using var session = theStore.LightweightSession();
        session.Store(doc1, doc2, doc3);
        await session.SaveChangesAsync();

        var searchNames = new[] { "Jeremy", "Josh" };

        var ids = session
            .Query<DocWithLists>()
            .Where(x => x.Names.Any(_ => searchNames.Contains(_)))
            .Select(x => x.Id)
            .ToList();

        ids.Count.ShouldBe(2);
        ids.ShouldContain(doc1.Id);
        ids.ShouldContain(doc2.Id);
    }

    [Fact]
    public async Task can_do_any_with_contains_against_IList_with_camel_casing()
    {
        StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

        var doc1 = new DocWithLists { Names = new List<string> { "Jeremy", "Josh", "Corey" } };
        var doc2 = new DocWithLists { Names = new List<string> { "Jeremy", "Lindsey", "Max" } };
        var doc3 = new DocWithLists { Names = new List<string> { "Jack", "Lindsey", "Max" } };

        using var session = theStore.LightweightSession();
        session.Store(doc1, doc2, doc3);
        await session.SaveChangesAsync();

        var searchNames = new[] { "Jeremy", "Josh" };

        var ids = session
            .Query<DocWithLists>()
            .Where(x => x.Names.Any(_ => searchNames.Contains(_)))
            .Select(x => x.Id)
            .ToList();

        ids.Count.ShouldBe(2);
        ids.ShouldContain(doc1.Id);
        ids.ShouldContain(doc2.Id);
    }

}

public class DocWithLists
{
    public Guid Id { get; set; }

    public IList<string> Names { get; set; } = new List<string>();
}
