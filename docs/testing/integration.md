# Integration testing

This documentation aims to guide you through the process of performing integration tests with Marten. We will go through setting up the host using [Alba](https://jasperfx.github.io/alba/), integrating with [Wolverine](https://wolverinefx.net), and testing event projections. The examples provided will leverage [Alba](https://jasperfx.github.io/alba/) and [xUnit](https://xunit.net/) for testing, but integration testing should be perfectly possible using Microsoft's [WebapplicationFactory](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests) and other testing frameworks like NUnit.

## Setting Up The Database Using Docker

Although you could spin up a testing [PostgreSQL](https://www.postgresql.org/) any way you want, our prefered way of running integration tests is running a [PostgreSQL](https://www.postgresql.org/) in docker. All that's needed is a `docker-compose.yml`:

```yaml
version: '3'
services:
  postgresql:
    image: "postgres:latest"
    ports:
     - "5433:5432"
    environment:
      - POSTGRES_DATABASE=postgres
      - POSTGRES_USER=postgres
      - POSTGRES_PASSWORD=postgres
```

Before running all test, just run 

```bash
docker compose up -d
```

## Setting Up The Host Using Alba

[Alba](https://jasperfx.github.io/alba/) is a friendly library for testing ASP.NET Core applications. To perform tests with MartenDB, it's essential to set up the host for the database first.

Firstly, install Alba via NuGet:

```bash
dotnet add package Alba
```

Then, set up your system under test (`AppFixture`) to use MartenDB:

<!-- snippet: sample_integration_appfixture -->
<a id='snippet-sample_integration_appfixture'></a>
```cs
public class AppFixture: IAsyncLifetime
{
    private string SchemaName { get; } = "sch" + Guid.NewGuid().ToString().Replace("-", string.Empty);
    public IAlbaHost Host { get; private set; }

    public async Task InitializeAsync()
    {
        // This is bootstrapping the actual application using
        // its implied Program.Main() set up
        Host = await AlbaHost.For<Program>(b =>
        {
            b.ConfigureServices((context, services) =>
            {
                services.Configure<MartenSettings>(s =>
                {
                    s.SchemaName = SchemaName;
                });
            });
        });
    }

    public async Task DisposeAsync()
        {
            await Host.DisposeAsync();
        }
    }
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/AppFixture.cs#L10-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_appfixture' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

To prevent spinning up the entire host (and database setup) for every test (in parallel) you could create a collection fixture to share between your tests:

<!-- snippet: sample_integration_collection -->
<a id='snippet-sample_integration_collection'></a>
```cs
[CollectionDefinition("integration")]
public class IntegrationCollection : ICollectionFixture<AppFixture>
{
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/IntegrationCollection.cs#L5-L10' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_collection' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For integration testing, It can be beneficial to a have a slim base class like this one:
<!-- snippet: sample_integration_context -->
<a id='snippet-sample_integration_context'></a>
```cs
public abstract class IntegrationContext : IAsyncLifetime
{
    protected IntegrationContext(AppFixture fixture)
    {
        Host = fixture.Host;
        Store = Host.Services.GetRequiredService<IDocumentStore>();
    }
     
    public IAlbaHost Host { get; }
    public IDocumentStore Store { get; }
     
    public async Task InitializeAsync()
    {
        // Using Marten, wipe out all data and reset the state.
        // Also restart the async daemon if in use.
        await Host.ResetAllMartenDataAsync();
    }
 
    // This is required because of the IAsyncLifetime 
    // interface. Note that I do *not* tear down database
    // state after the test. That's purposeful
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/IntegrationContext.cs#L8-L34' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Other than simply connecting real test fixtures to the ASP.Net Core system under test (the IAlbaHost), this `IntegrationContext` utilizes another bit of Marten functionality to completely reset the database state and then (re) applying the configured initial data so that we always have known data in the database before tests execute.

You can simplify the access to the `IDocumentStore` even more by calling the `DocumentStore` extension method on the `IHost`:

<!-- snippet: sample_simplified_integration_context -->
<a id='snippet-sample_simplified_integration_context'></a>
```cs
public abstract class SimplifiedIntegrationContext : IAsyncLifetime
{
    protected SimplifiedIntegrationContext(AppFixture fixture)
    {
        Host = fixture.Host;
        Store = Host.DocumentStore();
    }

    public IAlbaHost Host { get; }
    public IDocumentStore Store { get; }

    public async Task InitializeAsync()
    {
        // Using Marten, wipe out all data and reset the state
        await Store.Advanced.ResetAllData();
    }

    // This is required because of the IAsyncLifetime
    // interface. Note that I do *not* tear down database
    // state after the test. That's purposeful
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/Examples/SimplifiedIntegrationContext.cs#L7-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_simplified_integration_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If you're working with [multiple Marten databases](/configuration/hostbuilder#working-with-multiple-marten-databases), you can use the `IDocumentStore` extension method to get the store by its interface type:

<!-- snippet: sample_multiple_databases_integration_context -->
<a id='snippet-sample_multiple_databases_integration_context'></a>
```cs
public interface IInvoicingStore: IDocumentStore
{
}

public abstract class MultipleMartenDatabasesIntegrationContext: IAsyncLifetime
{
    protected MultipleMartenDatabasesIntegrationContext(
        AppFixture fixture
    )
    {
        Host = fixture.Host;
        Store = Host.DocumentStore();
        InvoicingStore = Host.DocumentStore<IInvoicingStore>();
    }

    public IAlbaHost Host { get; }
    public IDocumentStore Store { get; }
    public IInvoicingStore InvoicingStore { get; }

    public async Task InitializeAsync()
    {
        // Using Marten, wipe out all data and reset the state
        await Store.Advanced.ResetAllData();
    }

    // This is required because of the IAsyncLifetime
    // interface. Note that I do *not* tear down database
    // state after the test. That's purposeful
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/Examples/MultipleMartenDatabasesIntegrationContext.cs#L7-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_multiple_databases_integration_context' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Integration test example

Finally, in your xUnit test file, the actual example using the `IntegrationContext` and `AppFixture` we setup before:

<!-- snippet: sample_integration_streaming_example -->
<a id='snippet-sample_integration_streaming_example'></a>
```cs
[Collection("integration")]
public class web_service_streaming_example: IntegrationContext
{
    private readonly IAlbaHost theHost;

    public web_service_streaming_example(AppFixture fixture) : base(fixture)
    {
        theHost = fixture.Host;
    }

    [Fact]
    public async Task stream_a_single_document_hit()
    {
        var issue = new Issue {Description = "It's bad"};

        await using (var session = Store.LightweightSession())
        {
            session.Store(issue);
            await session.SaveChangesAsync();
        }

        var result = await theHost.Scenario(s =>
        {
            s.Get.Url($"/issue/{issue.Id}");

            s.StatusCodeShouldBe(200);
            s.ContentTypeShouldBe("application/json");
        });

        var read = result.ReadAsJson<Issue>();

        read.Description.ShouldBe(issue.Description);
    }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/Examples/web_service_streaming_tests.cs#L9-L44' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_streaming_example' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Set-up a new database scheme for every test to avoid database cleanup

To generate a scheme name for every test you could add this to your `AppFixture` class to generate a scheme name:

<!-- snippet: sample_integration_scheme_name -->
<a id='snippet-sample_integration_scheme_name'></a>
```cs
private string SchemaName { get; } = "sch" + Guid.NewGuid().ToString().Replace("-", string.Empty);
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/AppFixture.cs#L13-L15' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_scheme_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

SchemaName can not contain certain characters such as `-` and can not start with a number, so that's why it is not just a `Guid``.

You can configure your host to use this scheme name like this:

<!-- snippet: sample_integration_configure_scheme_name -->
<a id='snippet-sample_integration_configure_scheme_name'></a>
```cs
Host = await AlbaHost.For<Program>(b =>
{
    b.ConfigureServices((context, services) =>
    {
        services.Configure<MartenSettings>(s =>
        {
            s.SchemaName = SchemaName;
        });
    });
});
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/Marten.AspNetCore.Testing/AppFixture.cs#L22-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_configure_scheme_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`MartenSettings` is a custom config class, you can customize any way you'd like:

<!-- snippet: sample_integration_settings -->
<a id='snippet-sample_integration_settings'></a>
```cs
public class MartenSettings
{
    public const string SECTION = "Marten";
    public string SchemaName { get; set; }
}
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/MartenSettings.cs#L3-L9' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_settings' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Now in your actual application you should configure the schema name:

<!-- snippet: sample_integration_use_scheme_name -->
<a id='snippet-sample_integration_use_scheme_name'></a>
```cs
services.AddMarten(sp =>
{
    var options = new StoreOptions();
    options.Connection(ConnectionSource.ConnectionString);
    var martenSettings = sp.GetRequiredService<IOptions<MartenSettings>>().Value;

    if (!string.IsNullOrEmpty(martenSettings.SchemaName))
    {
        options.Events.DatabaseSchemaName = martenSettings.SchemaName;
        options.DatabaseSchemaName = martenSettings.SchemaName;
    }

    return options;
}).UseLightweightSessions();
```
<sup><a href='https://github.com/JasperFx/marten/blob/master/src/IssueService/Startup.cs#L32-L47' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample_integration_use_scheme_name' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

::: warning
Keep note that Marten can be configured to generate static code on startup that contains the scheme name, so it could be beneficial to keep that turned off in the tests.
:::

Alba hosts by default start with `ASPNETCORE_ENVIRONMENT=Development`, so the `AddMarten().OptimizeArtifactWorkflow()` option will not generate static code in that case as is explained here: [Development versus Production Usage](/configuration/optimized_artifact_workflow).

## Integrating with Wolverine

Whenever wolverine's messaging is used within your application, actions may be delayed. Luckily there is a method to await Wolverine processing like this:

```cs
// This method allows us to make HTTP calls into our system
// in memory with Alba, but do so within Wolverine's test support
// for message tracking to both record outgoing messages and to ensure
// that any cascaded work spawned by the initial command is completed
// before passing control back to the calling test
protected async Task<(ITrackedSession, IScenarioResult)> TrackedHttpCall(Action<Scenario> configuration)
{
    IScenarioResult result = null;
     
    // The outer part is tying into Wolverine's test support
    // to "wait" for all detected message activity to complete
    var tracked = await Host.ExecuteAndWaitAsync(async () =>
    {
        // The inner part here is actually making an HTTP request
        // to the system under test with Alba
        result = await Host.Scenario(configuration);
    });
 
    return (tracked, result);
}
```

Just add above to your `IntegrationContext` class and you'll be able to execute wolverine endpoints like: 

```cs
var (tracked, _) = await TrackedHttpCall(x =>
{
    // Send a JSON post with the DebitAccount command through the HTTP endpoint
    // BUT, it's all running in process
    x.Post.Json(new WithdrawFromAccount(account.Id, 1300)).ToUrl("/accounts/debit");

    // This is the default behavior anyway, but still good to show it here
    x.StatusCodeShouldBeOk();
});

// And also assert that an AccountUpdated message was published as well
var updated = tracked.Sent.SingleMessage<AccountUpdated>();
updated.AccountId.ShouldBe(account.Id);
updated.Balance.ShouldBe(1300);
```

Furthermore it could be beneficial to disable all external wolverine transports to test in isolation, by adding this to the Alba host setup:

```cs
services.DisableAllExternalWolverineTransports();
```

## Testing Projections

Testing all types of projections (live, inline, async) is explained here: [Testing Projections](/events/projections/testing).

## Additional Tips

1. **Parallel Execution**: xUnit runs tests in parallel. If your tests are not isolated, it could lead to unexpected behavior.
2. **Database Clean-Up**: You may want to clean up or reset the database state before running each test. Helpers are explained here: [Cleaning up database](/schema/cleaning).

Feel fre
