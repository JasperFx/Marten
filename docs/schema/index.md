# Marten and the PostgreSQL Schema

Marten works by adding tables and functions (yes, Virginia, we've let stored procedures creep back into our life) to a PostgreSQL schema. Marten will generate and add a table and matching `upsert` function for each unique document type as needed. It also adds some other tables and functions for the [event store functionality](/events/) and [HiLo id generation](/documents/identity)

In all cases, the Marten schema objects are all prefixed with `mt_.`

As of Marten v0.8, you have much finer grained ability to control the automatic generation or updates of schema objects through the
`StoreOptions.AutoCreateSchemaObjects` like so:

<!-- snippet: sample_AutoCreateSchemaObjects -->
<a id='snippet-sample_autocreateschemaobjects'></a>
```cs
var store = DocumentStore.For(opts =>
{
    // Marten will create any new objects that are missing,
    // attempt to update tables if it can, but drop and replace
    // tables that it cannot patch.
    opts.AutoCreateSchemaObjects = AutoCreate.All;

    // Marten will create any new objects that are missing or
    // attempt to update tables if it can. Will *never* drop
    // any existing objects, so no data loss
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    // Marten will create missing objects on demand, but
    // will not change any existing schema objects
    opts.AutoCreateSchemaObjects = AutoCreate.CreateOnly;

    // Marten will not create or update any schema objects
    // and throws an exception in the case of a schema object
    // not reflecting the Marten configuration
    opts.AutoCreateSchemaObjects = AutoCreate.None;
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/StoreOptionsTests.cs#L39-L63' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_autocreateschemaobjects' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To prevent unnecessary loss of data, even in development, on the first usage of a document type, Marten will:

1. Compare the current schema table to what's configured for that document type
2. If the table matches, do nothing
3. If the table is missing, try to create the table depending on the auto create schema setting shown above
4. If the table has new, searchable columns, adds the new column and runs an "UPDATE" command to duplicate the
   information in the JsonB data field. Do note that this could be expensive for large tables. This is also impacted
   by the auto create schema mode shown above.

Our thought is that in development you probably run in the "All" mode, but in production use one of the more restrictive auto creation modes.

**As of Marten v0.9.2, Marten will also check if the existing _upsert_ function and any table indexes match
what is configured in the document store, and attempts to update these objects if necessary based on the same
All/None/CreateOnly/CreateOrUpdate rules as the table storage.**

## Overriding Schema Name

By default marten will use the default `public` database scheme to create the document tables and function. You may, however, choose to set a different document store database schema name, like so:

```cs
StoreOptions.DatabaseSchemaName = "other";
```

The `Hilo` sequence table is always created in this document store database schema.

If you wish to assign certain document tables to different (new or existing) schemas, you can do so like that:

```cs
StoreOptions.Schema.For<User>().DatabaseSchemaName("other");
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Schema.Testing/DocumentSchemaTests.cs#L154-L167' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_override_schema_per_table' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This will create the following tables in your database: `other.mt_doc_user`, `overriden.mt_doc_issue` and `public.mt_doc_company`. When a schema doesn't exist it will be generated in the database.

### Event Store

The EventStore database object are by default created in the document store DatabaseSchemaName. This can be overridden by setting the DatabaseSchemaName property of the event store options.

```cs
StoreOptions.Events.DatabaseSchemaName = "event_store";
```

This will ensure that all EventStore tables (mt_stream, mt_events, ...) and functions (mt_apply_transform, mt_apply_aggregation, ...) are created in the `event_store` schema.

## Create Database

::: warning
You will probably need to use the `AddMarten().ApplyAllDatabaseChangesOnStartup()` option to force Marten to check and build additional databases on start up times by registering
an `IHostedService` into your system that will run on startup.
:::

Marten can be configured to create (or drop & create) databases in case they do not exist. This is done via store options, through `StoreOptions.CreateDatabasesForTenants`.

<!-- snippet: sample_marten_create_database -->
<a id='snippet-sample_marten_create_database'></a>
```cs
storeOptions.CreateDatabasesForTenants(c =>
{
    // Specify a db to which to connect in case database needs to be created.
    // If not specified, defaults to 'postgres' on the connection for a tenant.
    c.MaintenanceDatabase(cstring);
    c.ForTenant()
        .CheckAgainstPgDatabase()
        .WithOwner("postgres")
        .WithEncoding("UTF-8")
        .ConnectionLimit(-1)
        .OnDatabaseCreated(_ =>
        {
            dbCreated = true;
        });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/CoreTests/create_database_Tests.cs#L42-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_marten_create_database' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Databases are checked for existence upon store initialization. By default, connection attempts are made against the databases specified for tenants. If a connection attempt results in an invalid catalog error (3D000), database creation is triggered. `ITenantDatabaseCreationExpressions.CheckAgainstPgDatabase` can be used to alter this behavior to check for database existence from `pg_database`.

Note that database creation requires the `CREATEDB` privilege. See PostgreSQL [CREATE DATABASE](https://www.postgresql.org/docs/current/static/sql-createdatabase.html) documentation for more.
