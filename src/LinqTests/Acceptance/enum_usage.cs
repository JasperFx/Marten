﻿using System.Linq;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class enum_usage : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public enum_usage(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void use_enum_values_with_jil_that_are_not_duplicated()
    {
        TheSession.Store(new Target{Color = Colors.Blue, Number = 1});
        TheSession.Store(new Target{Color = Colors.Red, Number = 2});
        TheSession.Store(new Target{Color = Colors.Green, Number = 3});
        TheSession.Store(new Target{Color = Colors.Blue, Number = 4});
        TheSession.Store(new Target{Color = Colors.Red, Number = 5});
        TheSession.Store(new Target{Color = Colors.Green, Number = 6});
        TheSession.Store(new Target{Color = Colors.Blue, Number = 7});

        TheSession.SaveChanges();

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }

    [Fact]
    public void use_enum_values_with_newtonsoft_that_are_not_duplicated()
    {
        StoreOptions(_ => _.Serializer<JsonNetSerializer>());

        TheSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 2 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 3 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 4 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 5 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 6 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 7 });

        TheSession.SaveChanges();

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }

    [Fact]
    public void use_enum_values_with_newtonsoft_that_are_not_duplicated_and_stored_as_strings()
    {
        StoreOptions(_ => _.Serializer(new JsonNetSerializer {EnumStorage = EnumStorage.AsString}));

        TheSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 2 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 3 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 4 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 5 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 6 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 7 });

        TheSession.SaveChanges();

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }


    [Fact]
    public void use_enum_values_with_jil_that_are_duplicated()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(x => x.Color);
        });

        TheSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 2 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 3 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 4 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 5 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 6 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 7 });

        TheSession.SaveChanges();

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }

    [Fact]
    public void use_enum_values_with_newtonsoft_that_are_duplicated()
    {
        StoreOptions(_ =>
        {
            _.Serializer<JsonNetSerializer>();
            _.Schema.For<Target>().Duplicate(x => x.Color);
        });

        TheSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 2 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 3 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 4 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 5 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 6 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 7 });

        TheSession.SaveChanges();

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }

    [Fact]
    public void use_enum_values_with_newtonsoft_that_are_duplicated_as_string_storage()
    {
        StoreOptions(_ =>
        {
            _.UseDefaultSerialization(EnumStorage.AsString);
            _.Schema.For<Target>().Duplicate(x => x.Color);
        });

        TheSession.Store(new Target { Color = Colors.Blue, Number = 1 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 2 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 3 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 4 });
        TheSession.Store(new Target { Color = Colors.Red, Number = 5 });
        TheSession.Store(new Target { Color = Colors.Green, Number = 6 });
        TheSession.Store(new Target { Color = Colors.Blue, Number = 7 });

        TheSession.SaveChanges();

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }




    [Fact]
    public void use_enum_values_with_jil_that_are_duplicated_with_bulk_import()
    {
        StoreOptions(_ =>
        {
            _.Schema.For<Target>().Duplicate(x => x.Color);
        });

        var targets = new Target[]
        {
            new Target {Color = Colors.Blue, Number = 1},
            new Target {Color = Colors.Red, Number = 2},
            new Target {Color = Colors.Green, Number = 3},
            new Target {Color = Colors.Blue, Number = 4},
            new Target {Color = Colors.Red, Number = 5},
            new Target {Color = Colors.Green, Number = 6},
            new Target {Color = Colors.Blue, Number = 7}
        };

        TheStore.BulkInsert(targets);

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }

    [Fact]
    public void use_enum_values_with_newtonsoft_that_are_duplicated_with_bulk_import()
    {
        StoreOptions(_ =>
        {
            _.Serializer<JsonNetSerializer>();
            _.Schema.For<Target>().Duplicate(x => x.Color);
        });

        var targets = new Target[]
        {
            new Target {Color = Colors.Blue, Number = 1},
            new Target {Color = Colors.Red, Number = 2},
            new Target {Color = Colors.Green, Number = 3},
            new Target {Color = Colors.Blue, Number = 4},
            new Target {Color = Colors.Red, Number = 5},
            new Target {Color = Colors.Green, Number = 6},
            new Target {Color = Colors.Blue, Number = 7}
        };

        TheStore.BulkInsert(targets);

        TheSession.Query<Target>().Where(x => x.Color == Colors.Blue).ToArray()
            .Select(x => x.Number)
            .ShouldHaveTheSameElementsAs(1, 4, 7);
    }

    [Fact]
    public void use_nullable_enum_values_as_part_of_in_query()
    {
        TheSession.Store(new Target{NullableEnum = Colors.Green, Number = 1});
        TheSession.Store(new Target{NullableEnum = Colors.Blue, Number = 2});
        TheSession.Store(new Target{NullableEnum = Colors.Red, Number = 3});
        TheSession.Store(new Target{NullableEnum = Colors.Green, Number = 4});
        TheSession.Store(new Target{NullableEnum = null, Number = 5});
        TheSession.SaveChanges();

        var results = TheSession.Query<Target>().Where(x => x.NullableEnum.In(null, Colors.Green))
            .ToList();

        results.Count.ShouldBe(3);
    }

    [Fact]
    public void use_nullable_enum_values_as_part_of_notin_query()
    {
        TheSession.Store(new Target{NullableEnum = Colors.Green, Number = 1});
        TheSession.Store(new Target{NullableEnum = Colors.Blue, Number = 2});
        TheSession.Store(new Target{NullableEnum = Colors.Red, Number = 3});
        TheSession.Store(new Target{NullableEnum = Colors.Green, Number = 4});
        TheSession.Store(new Target{NullableEnum = null, Number = 5});
        TheSession.SaveChanges();

        var results = TheSession.Query<Target>().Where(x => !x.NullableEnum.In(null, Colors.Green))
            .ToList();

        results.Count.ShouldBe(2);
    }
}
