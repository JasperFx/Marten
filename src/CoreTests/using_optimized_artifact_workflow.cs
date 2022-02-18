using Baseline;
using Lamar;
using LamarCodeGeneration;
using Marten;
using Marten.Testing.Harness;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Weasel.Core;
using Xunit;

namespace CoreTests
{
    public class using_optimized_artifact_workflow
    {
        [Fact]
        public void all_the_defaults()
        {
            using var container = Container.For(services =>
            {
                services.AddMarten(ConnectionSource.ConnectionString);
            });


            var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();

            var rules = store.Options.CreateGenerationRules();

            store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
            store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Dynamic);

            rules.GeneratedNamespace.ShouldBe("Marten.Generated");
            rules.SourceCodeWritingEnabled.ShouldBeTrue();

        }

        [Fact]
        public void using_optimized_mode_in_development()
        {
            using var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow();
                })
                .UseEnvironment("Development")
                .Start();


            var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

            var rules = store.Options.CreateGenerationRules();

            store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.CreateOrUpdate);
            store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);

            rules.GeneratedNamespace.ShouldBe("Marten.Generated");
            rules.SourceCodeWritingEnabled.ShouldBeTrue();
        }

        [Fact]
        public void using_optimized_mode_in_production()
        {
            using var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow();
                })
                .UseEnvironment("Production")
                .Start();


            var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

            var rules = store.Options.CreateGenerationRules();

            store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);
            store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Auto);

            rules.GeneratedNamespace.ShouldBe("Marten.Generated");
            rules.SourceCodeWritingEnabled.ShouldBeFalse();
        }

        [Fact]
        public void using_optimized_mode_in_production_override_type_load_mode()
        {
            using var host = new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddMarten(ConnectionSource.ConnectionString).OptimizeArtifactWorkflow(TypeLoadMode.Static);
                })
                .UseEnvironment("Production")
                .Start();


            var store = host.Services.GetRequiredService<IDocumentStore>().As<DocumentStore>();

            var rules = store.Options.CreateGenerationRules();

            store.Options.AutoCreateSchemaObjects.ShouldBe(AutoCreate.None);
            store.Options.GeneratedCodeMode.ShouldBe(TypeLoadMode.Static);

            rules.GeneratedNamespace.ShouldBe("Marten.Generated");
            rules.SourceCodeWritingEnabled.ShouldBeFalse();
        }
    }
}
