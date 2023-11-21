using System;
using System.Linq;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Events.Projections.Flattened;
using Marten.Exceptions;
using Shouldly;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests.Projections.Flattened;

public class FlatTableProjectionTests
{
    [Fact]
    public void validation_fails_with_no_event_handlers()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.DocumentSchema);

        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());

        ex.Message.ShouldContain("Empty flat table projections", Case.Insensitive);
    }

    [Fact]
    public void validation_fails_with_no_primary_key()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.DocumentSchema);
        projection.Delete<ValuesDeleted>();

        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());

        ex.Message.ShouldContain("Flat table projections require a single column primary key", Case.Insensitive);
    }

    [Fact]
    public void validation_fails_with_multi_column_primary_key()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.DocumentSchema);
        projection.Table.AddColumn<string>("one").AsPrimaryKey();
        projection.Table.AddColumn<string>("two").AsPrimaryKey();

        projection.Delete<ValuesDeleted>();

        var ex = Should.Throw<InvalidProjectionException>(() => projection.AssembleAndAssertValidity());

        ex.Message.ShouldContain("Flat table projections require a single column primary key", Case.Insensitive);
    }

    [Fact]
    public void happy_path_validation_with_Guid_stream_identity()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.DocumentSchema);
        projection.Table.AddColumn<Guid>("id").AsPrimaryKey();
        projection.Delete<ValuesDeleted>();

        projection.AssembleAndAssertValidity();
    }

    [Fact]
    public void use_explicit_schema_name_1()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.DocumentSchema);
        projection.Table.AddColumn<Guid>("id").AsPrimaryKey();
        projection.Delete<ValuesDeleted>();

        var table = projection.As<IProjectionSchemaSource>()
            .CreateSchemaObjects(new EventGraph(new StoreOptions())).OfType<Table>().Single();

        table.Identifier.Schema.ShouldBe("public");
    }


    [Fact]
    public void use_explicit_schema_name_2()
    {
        var projection = new FlatTableProjection(new PostgresqlObjectName("special", "foo"));
        projection.Table.AddColumn<Guid>("id").AsPrimaryKey();
        projection.Delete<ValuesDeleted>();

        var table = projection.As<IProjectionSchemaSource>()
            .CreateSchemaObjects(new EventGraph(new StoreOptions())).OfType<Table>().Single();

        table.Identifier.Schema.ShouldBe("special");
    }

    [Fact]
    public void use_schema_from_document_store_options()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.DocumentSchema);
        projection.Table.AddColumn<Guid>("id").AsPrimaryKey();
        projection.Delete<ValuesDeleted>();

        var events = new EventGraph(new StoreOptions { DatabaseSchemaName = "FromDocStore" });

        var table = projection.As<IProjectionSchemaSource>()
            .CreateSchemaObjects(events).OfType<Table>().Single();

        table.Identifier.Schema.ShouldBe("fromdocstore");
    }


    [Fact]
    public void use_schema_from_event_graph()
    {
        var projection = new FlatTableProjection("foo", SchemaNameSource.EventSchema);
        projection.Table.AddColumn<Guid>("id").AsPrimaryKey();
        projection.Delete<ValuesDeleted>();

        var events = new EventGraph(new StoreOptions { DatabaseSchemaName = "fromdocstore" })
        {
            DatabaseSchemaName = "FromEventGraph"
        };

        var table = projection.As<IProjectionSchemaSource>()
            .CreateSchemaObjects(events).OfType<Table>().Single();

        table.Identifier.Schema.ShouldBe("fromeventgraph");
    }
}
