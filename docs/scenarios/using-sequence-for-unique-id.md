# Using sequences for unique and human-readable identifiers

This scenario demonstrates how to generate unique, human-readable (number) identifiers using Marten and PostgreSQL sequences.

## Scenario

Let us assume we have a system using types with non-human-readable identifiers (e.g. `Guid`) for internal system implementation. However, for end users we want to expose references to the said entities in a human-readable form. Furthermore, we need the identifiers to be unique and from a running positive sequence starting from 10000. This scenario demonstrates how to implement the described behavior using Marten and PostgreSQL sequences.

We first introduce a Marten schema customization type, deriving from `FeatureSchemaBase`:

<!-- snippet: sample_scenario-usingsequenceforuniqueid-setup -->
<a id='snippet-sample_scenario-usingsequenceforuniqueid-setup'></a>
```cs
// We introduce a new feature schema, making use of Marten's schema management facilities.
public class MatterId: FeatureSchemaBase
{
    private readonly int _startFrom;
    private readonly string _schema;

    public MatterId(StoreOptions options, int startFrom): base(nameof(MatterId), options.Advanced.Migrator)
    {
        _startFrom = startFrom;
        _schema = options.DatabaseSchemaName;
    }

    protected override IEnumerable<ISchemaObject> schemaObjects()
    {
        // We return a sequence that starts from the value provided in the ctor
        yield return new Sequence(new PostgresqlObjectName(_schema, $"mt_{nameof(MatterId).ToLowerInvariant()}"),
            _startFrom);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ScenarioUsingSequenceForUniqueId.cs#L15-L37' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-usingsequenceforuniqueid-setup' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

This sequence yielding customization will be plugged into Marten via the store configuration

<!-- snippet: sample_scenario-usingsequenceforuniqueid-storesetup-1 -->
<a id='snippet-sample_scenario-usingsequenceforuniqueid-storesetup-1'></a>
```cs
storeOptions.Storage.Add(new MatterId(storeOptions, 10000));
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ScenarioUsingSequenceForUniqueId.cs#L44-L48' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-usingsequenceforuniqueid-storesetup-1' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

and then executed against the database (generating & executing the DDL statements that create the required database objects):

<!-- snippet: sample_scenario-usingsequenceforuniqueid-storesetup-2 -->
<a id='snippet-sample_scenario-usingsequenceforuniqueid-storesetup-2'></a>
```cs
await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ScenarioUsingSequenceForUniqueId.cs#L51-L55' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-usingsequenceforuniqueid-storesetup-2' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

We introduce a few types with `Guid` identifiers, whom we reference to our end users by numbers, encapsulated in the `Matter` field:

<!-- snippet: sample_scenario-usingsequenceforuniqueid-setup-types -->
<a id='snippet-sample_scenario-usingsequenceforuniqueid-setup-types'></a>
```cs
public class Contract
{
    public Guid Id { get; set; }

    public int Matter { get; set; }
    // Other fields...
}

public class Inquiry
{
    public Guid Id { get; set; }

    public int Matter { get; set; }
    // Other fields...
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ScenarioUsingSequenceForUniqueId.cs#L77-L95' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-usingsequenceforuniqueid-setup-types' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now, when creating and persisting such types, we first query the database for a new and unique running number. While we generate (or if wanted, let Marten generate) non-human-readable, system-internal identifiers for the created instances, we assign to them the newly generated and unique human-readable identifier:

<!-- snippet: sample_scenario-usingsequenceforuniqueid-querymatter -->
<a id='snippet-sample_scenario-usingsequenceforuniqueid-querymatter'></a>
```cs
var matter = theStore.StorageFeatures.FindFeature(typeof(MatterId)).Objects.OfType<Sequence>().Single();

await using var session = theStore.LightweightSession();
// Generate a new, unique identifier
var nextMatter = session.NextInSequence(matter);

var contract = new Contract { Id = Guid.NewGuid(), Matter = nextMatter };

var inquiry = new Inquiry { Id = Guid.NewGuid(), Matter = nextMatter };

session.Store(contract);
session.Store(inquiry);

await session.SaveChangesAsync();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ScenarioUsingSequenceForUniqueId.cs#L57-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-usingsequenceforuniqueid-querymatter' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lastly, we have an extension method (used above) as a shorthand for generating the SQL statement for a sequence value query:

<!-- snippet: sample_scenario-usingsequenceforuniqueid-setup-extensions -->
<a id='snippet-sample_scenario-usingsequenceforuniqueid-setup-extensions'></a>
```cs
public static class SessionExtensions
{
    // A shorthand for generating the required SQL statement for a sequence value query
    public static int NextInSequence(this IQuerySession session, Sequence sequence)
    {
        return session.Query<int>("select nextval(?)", sequence.Identifier.QualifiedName).First();
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.Testing/Examples/ScenarioUsingSequenceForUniqueId.cs#L98-L109' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_scenario-usingsequenceforuniqueid-setup-extensions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
