#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Exceptions;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.Fields;
using Marten.Metadata;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Services.Json;
using Marten.Storage;
using Npgsql;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;

namespace Marten;

/// <summary>
///     StoreOptions supplies all the necessary configuration
///     necessary to customize and bootstrap a working
///     DocumentStore
/// </summary>
public partial class StoreOptions: IReadOnlyStoreOptions, IMigrationLogger
{
    internal readonly List<Action<ISerializer>> SerializationConfigurations = new();

    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, ChildDocument>> _childDocs
        = new();

    private readonly IList<IDocumentPolicy> _policies = new List<IDocumentPolicy>
    {
        new VersionedPolicy(), new SoftDeletedPolicy(), new TrackedPolicy(), new TenancyPolicy()
    };

    /// <summary>
    ///     Register "initial data loads" that will be applied to the DocumentStore when it is
    ///     bootstrapped
    /// </summary>
    internal readonly IList<IInitialData> InitialData = new List<IInitialData>();

    /// <summary>
    ///     Add, remove, or reorder global session listeners
    /// </summary>
    public readonly IList<IDocumentSessionListener> Listeners = new List<IDocumentSessionListener>();

    /// <summary>
    ///     Modify the document and event store database mappings for indexes and searching options
    /// </summary>
    public readonly MartenRegistry Schema;

    private ImHashMap<Type, IFieldMapping> _childFieldMappings = ImHashMap<Type, IFieldMapping>.Empty;

    private string _databaseSchemaName = SchemaConstants.DefaultSchema;

    private IMartenLogger _logger = new NulloMartenLogger();

    private IRetryPolicy _retryPolicy = new NulloRetryPolicy();
    private ISerializer? _serializer;

    /// <summary>
    ///     Whether or Marten should attempt to create any missing database schema objects at runtime. This
    ///     property is "CreateOrUpdate" by default for more efficient development, but can be set to lower values for production usage.
    /// </summary>
    public AutoCreate AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;

    public StoreOptions()
    {
        EventGraph = new EventGraph(this);
        Schema = new MartenRegistry(this);
        Storage = new StorageFeatures(this);

        Providers = new ProviderGraph(this);
        Advanced = new AdvancedOptions(this);

        Projections = new ProjectionOptions(this);
    }

    internal IList<Type> CompiledQueryTypes { get; } = new List<Type>();

    /// <summary>
    ///     Advisory lock id is used by the ApplyChangesOnStartup() option to serialize access to making
    ///     schema changes from multiple application nodes
    /// </summary>
    public int ApplyChangesLockId { get; set; } = 4004;

    /// <summary>
    ///     Used internally by the MartenActivator
    /// </summary>
    internal bool ShouldApplyChangesOnStartup { get; set; } = false;

    /// <summary>
    ///     Used internally by the MartenActivator
    /// </summary>
    internal bool ShouldAssertDatabaseMatchesConfigurationOnStartup { get; set; } = false;

    /// <summary>
    ///     Configuration for all event store projections
    /// </summary>
    public ProjectionOptions Projections { get; }

    /// <summary>
    ///     Direct Marten to either generate code at runtime (Dynamic), or attempt to load types from the entry assembly
    /// </summary>
    public TypeLoadMode GeneratedCodeMode { get; set; } = TypeLoadMode.Dynamic;

    /// <summary>
    ///     Access to adding custom schema features to this Marten-enabled Postgresql database
    /// </summary>
    public StorageFeatures Storage { get; }

    internal Action<IDatabaseCreationExpressions>? CreateDatabases { get; set; }

    internal IProviderGraph Providers { get; }

    /// <summary>
    ///     Advanced configuration options for this DocumentStore
    /// </summary>
    public AdvancedOptions Advanced { get; }

    internal EventGraph EventGraph { get; }

    /// <summary>
    ///     Configuration of event streams and projections
    /// </summary>
    public IEventStoreOptions Events => EventGraph;

    /// <summary>
    ///     Extension point to add custom Linq query parsers
    /// </summary>
    public LinqParsing Linq { get; } = new();

    /// <summary>
    ///     Apply conventional policies to how documents are mapped
    /// </summary>
    public PoliciesExpression Policies => new(this);


    void IMigrationLogger.SchemaChange(string sql)
    {
        Logger().SchemaChange(sql);
    }

    void IMigrationLogger.OnFailure(DbCommand command, Exception ex)
    {
        throw new MartenSchemaException("All Configured Changes", command.CommandText, ex);
    }

    IReadOnlyAdvancedOptions IReadOnlyStoreOptions.Advanced => Advanced;

    /// <summary>
    ///     Sets the database default schema name used to store the documents.
    /// </summary>
    public string DatabaseSchemaName
    {
        get => _databaseSchemaName;
        set => _databaseSchemaName = value.ToLowerInvariant();
    }

    /// <summary>
    ///     Used to validate database object name lengths against Postgresql's NAMEDATALEN property to avoid
    ///     Marten getting confused when comparing database schemas against the configuration. See
    ///     https://www.postgresql.org/docs/current/static/sql-syntax-lexical.html
    ///     for more information. This does NOT adjust NAMEDATALEN for you.
    /// </summary>
    public int NameDataLength
    {
        get => Advanced.Migrator.NameDataLength;
        set => Advanced.Migrator.NameDataLength = value;
    }

    /// <summary>
    ///     Gets Enum values stored as either integers or strings. This is configured on your ISerializer
    /// </summary>
    public EnumStorage EnumStorage => Serializer().EnumStorage;


    /// <summary>
    ///     Sets the batch size for updating or deleting documents in IDocumentSession.SaveChanges() /
    ///     IUnitOfWork.ApplyChanges()
    /// </summary>
    public int UpdateBatchSize { get; set; } = 500;

    /// <summary>
    ///     Retrieve the currently configured serializer
    /// </summary>
    /// <returns></returns>
    public ISerializer Serializer()
    {
        if (_serializer == null)
        {
            _serializer = SerializerFactory.New();
            foreach (var configure in SerializationConfigurations)
            {
                configure(_serializer);
            }
        }

        return _serializer;
    }

    /// <summary>
    ///     Retrieve the currently configured logger for this DocumentStore
    /// </summary>
    /// <returns></returns>
    public IMartenLogger Logger()
    {
        return _logger ?? new NulloMartenLogger();
    }

    /// <summary>
    ///     Retrieve the current retry policy for this DocumentStore
    /// </summary>
    /// <returns></returns>
    public IRetryPolicy RetryPolicy()
    {
        return _retryPolicy ?? new NulloRetryPolicy();
    }

    IReadOnlyList<IDocumentType> IReadOnlyStoreOptions.AllKnownDocumentTypes()
    {
        return Storage.AllDocumentMappings.OfType<IDocumentType>().ToList();
    }


    IDocumentType IReadOnlyStoreOptions.FindOrResolveDocumentType(Type documentType)
    {
        return (Storage.FindMapping(documentType).Root as IDocumentType)!;
    }

    /// <summary>
    ///     Get or set the tenancy model for this DocumentStore
    /// </summary>
    public ITenancy Tenancy { get; set; } = null!;

    IReadOnlyEventStoreOptions IReadOnlyStoreOptions.Events => EventGraph;

    IReadOnlyLinqParsing IReadOnlyStoreOptions.Linq => Linq;

    /// <summary>
    ///     Configure Marten to create databases for tenants in case databases do not exist or need to be dropped & re-created.
    /// You will need to also use the ApplyAllDatabaseChangesOnStartup() option when configuring Marten to make this function correctly
    /// </summary>
    /// <remarks>Creating and dropping databases requires the CREATEDB privilege</remarks>
    public void CreateDatabasesForTenants(Action<IDatabaseCreationExpressions> configure)
    {
        CreateDatabases = configure ?? throw new ArgumentNullException(nameof(configure));
    }


    /// <summary>
    ///     Supply the connection string to the Postgresql database
    /// </summary>
    /// <param name="connectionString"></param>
    public void Connection(string connectionString)
    {
        Tenancy = new DefaultTenancy(new ConnectionFactory(connectionString), this);
    }

    /// <summary>
    ///     Supply a source for the connection string to a Postgresql database
    /// </summary>
    /// <param name="connectionSource"></param>
    public void Connection(Func<string> connectionSource)
    {
        Tenancy = new DefaultTenancy(new ConnectionFactory(connectionSource), this);
    }

    /// <summary>
    ///     Supply a mechanism for resolving an NpgsqlConnection object to
    ///     the Postgresql database
    /// </summary>
    /// <param name="source"></param>
    public void Connection(Func<NpgsqlConnection> source)
    {
        Tenancy = new DefaultTenancy(new LambdaConnectionFactory(source), this);
    }

    /// <summary>
    ///     Override the JSON serialization by ISerializer type
    /// </summary>
    /// <param name="serializer"></param>
    public void Serializer(ISerializer serializer)
    {
        _serializer = serializer;

        // Reapply any serialization addons
        foreach (var configure in SerializationConfigurations)
        {
            configure(_serializer);
        }
    }

    /// <summary>
    ///     Use the default serialization (ilmerged Newtonsoft.Json) with Enum values
    ///     stored as either integers or strings
    /// </summary>
    /// <param name="enumStorage"></param>
    /// <param name="casing">Casing style to be used in serialization</param>
    /// <param name="collectionStorage">Allow to set collection storage as raw arrays (without explicit types)</param>
    /// <param name="nonPublicMembersStorage">Allow non public members to be used during deserialization</param>
    public void UseDefaultSerialization(
        EnumStorage enumStorage = EnumStorage.AsInteger,
        Casing casing = Casing.Default,
        CollectionStorage collectionStorage = CollectionStorage.Default,
        NonPublicMembersStorage nonPublicMembersStorage = NonPublicMembersStorage.Default,
        SerializerType serializerType = SerializerType.Newtonsoft
    )
    {
        var serializer = SerializerFactory.New(serializerType,
            new SerializerOptions
            {
                EnumStorage = enumStorage,
                Casing = casing,
                CollectionStorage = collectionStorage,
                NonPublicMembersStorage = nonPublicMembersStorage
            });

        Serializer(serializer);
    }

    /// <summary>
    ///     Override the JSON serialization by an ISerializer of type "T"
    /// </summary>
    /// <typeparam name="T">The ISerializer type</typeparam>
    public void Serializer<T>() where T : ISerializer, new()
    {
        _serializer = new T();
        foreach (var configuration in SerializationConfigurations)
        {
            configuration(_serializer);
        }
    }

    /// <summary>
    ///     Replace the Marten logging strategy
    /// </summary>
    /// <param name="logger"></param>
    public void Logger(IMartenLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    ///     Replace the Marten retry policy
    /// </summary>
    /// <param name="retryPolicy"></param>
    public void RetryPolicy(IRetryPolicy retryPolicy)
    {
        _retryPolicy = retryPolicy;
    }

    /// <summary>
    ///     Force Marten to create document mappings for type T
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void RegisterDocumentType<T>()
    {
        RegisterDocumentType(typeof(T));
    }

    /// <summary>
    ///     Force Marten to create a document mapping for the document type
    /// </summary>
    /// <param name="documentType"></param>
    public void RegisterDocumentType(Type documentType)
    {
        Storage.RegisterDocumentType(documentType);
    }

    /// <summary>
    ///     Force Marten to create document mappings for all the given document types
    /// </summary>
    /// <param name="documentTypes"></param>
    public void RegisterDocumentTypes(IEnumerable<Type> documentTypes)
    {
        documentTypes.Each(RegisterDocumentType);
    }

    /// <summary>
    ///     Register a compiled query type for the "generate ahead" code generation strategy
    /// </summary>
    /// <param name="queryType"></param>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void RegisterCompiledQueryType(Type queryType)
    {
        if (!queryType.Closes(typeof(ICompiledQuery<,>)))
        {
            throw new ArgumentOutOfRangeException(nameof(queryType),
                $"{queryType.FullNameInCode()} is not a valid Marten compiled query type");
        }

        if (!queryType.HasDefaultConstructor())
        {
            throw new ArgumentOutOfRangeException(nameof(queryType),
                "Sorry, but Marten requires a no-arg constructor on compiled query types in order to opt into the 'code ahead' generation model.");
        }

        CompiledQueryTypes.Fill(queryType);
    }

    internal void ApplyConfiguration()
    {
        Storage.BuildAllMappings();

        Schema.For<DeadLetterEvent>().DatabaseSchemaName(Events.DatabaseSchemaName).SingleTenanted();

        foreach (var mapping in Storage.AllDocumentMappings) mapping.CompileAndValidate();
    }

    internal void applyPolicies(DocumentMapping mapping)
    {
        foreach (var policy in _policies) policy.Apply(mapping);
    }

    /// <summary>
    ///     Validate that minimal options to initialize a document store have been specified
    /// </summary>
    internal void Validate()
    {
        if (Tenancy == null)
        {
            throw new InvalidOperationException(
                "Tenancy not specified - provide either connection string or connection factory through Connection(..)");
        }
    }

    /// <summary>
    ///     These mappings should only be used for Linq querying within the SelectMany() body
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    internal IFieldMapping ChildTypeMappingFor(Type type)
    {
        if (_childFieldMappings.TryFind(type, out var mapping))
        {
            return mapping;
        }

        mapping = new FieldMapping("d.data", type, this);

        _childFieldMappings = _childFieldMappings.AddOrUpdate(type, mapping);

        return mapping;
    }

    /// <summary>
    ///     Meant for testing scenarios to "help" .Net understand where the IHostEnvironment for the
    ///     Host. You may have to specify the relative path to the entry project folder from the AppContext.BaseDirectory
    /// </summary>
    /// <param name="services"></param>
    /// <param name="assembly"></param>
    /// <param name="hintPath"></param>
    /// <returns></returns>
    public void SetApplicationProject(Assembly assembly,
        string? hintPath = null)
    {
        ApplicationAssembly = assembly ?? throw new ArgumentNullException(nameof(assembly));

        // TODO -- pull this into LamarCodeGeneration itself.
        var path = AppContext.BaseDirectory.ToFullPath();
        if (hintPath.IsNotEmpty())
        {
            path = path.AppendPath(hintPath).ToFullPath();
        }
        else
        {
            try
            {
                path = path.TrimEnd(Path.DirectorySeparatorChar);
                while (!path.EndsWith("bin"))
                {
                    path = path.ParentDirectory();
                }

                // Go up once to get to the test project directory, then up again to the "src" level,
                // then "down" to the application directory
                path = path.ParentDirectory().ParentDirectory().AppendPath(assembly.GetName().Name);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to determine the ");
                Console.WriteLine(e);
                path = AppContext.BaseDirectory.ToFullPath();
            }
        }

        GeneratedCodeOutputPath = path.AppendPath("Internal", "Generated");
    }

    /// <summary>
    ///     Opt into a multi-tenancy per database strategy where all databases
    ///     are on the same Postgresql server instance
    /// </summary>
    /// <param name="masterConnectionString"></param>
    /// <returns></returns>
    public ISingleServerMultiTenancy MultiTenantedWithSingleServer(string masterConnectionString)
    {
        Advanced.DefaultTenantUsageEnabled = false;
        var tenancy = new SingleServerMultiTenancy(masterConnectionString, this);
        Tenancy = tenancy;

        return tenancy;
    }

    /// <summary>
    ///     Opt into multi-tenancy per database strategy where all the
    ///     databases and tenants have to be statically configured at
    ///     bootstrapping time
    /// </summary>
    /// <param name="configure"></param>
    public void MultiTenantedDatabases(Action<IStaticMultiTenancy> configure)
    {
        Advanced.DefaultTenantUsageEnabled = false;
        var tenancy = new StaticMultiTenancy(this);
        Tenancy = tenancy;

        configure(tenancy);
    }

    public class PoliciesExpression
    {
        private readonly StoreOptions _parent;

        public PoliciesExpression(StoreOptions parent)
        {
            _parent = parent;
        }

        /// <summary>
        ///     Add a pre-built Marten document policy
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public PoliciesExpression OnDocuments<T>() where T : IDocumentPolicy, new()
        {
            return OnDocuments(new T());
        }

        /// <summary>
        ///     Add a pre-built Marten document policy
        /// </summary>
        /// <param name="policy"></param>
        /// <returns></returns>
        public PoliciesExpression OnDocuments(IDocumentPolicy policy)
        {
            _parent._policies.Insert(0, policy);
            return this;
        }

        /// <summary>
        ///     Apply configuration to the persistence of all Marten document
        ///     types
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public PoliciesExpression ForAllDocuments(Action<DocumentMapping> configure)
        {
            return OnDocuments(new LambdaDocumentPolicy(configure));
        }

        /// <summary>
        ///     Unless explicitly marked otherwise, all documents should
        ///     use conjoined multi-tenancy
        /// </summary>
        /// <returns></returns>
        public PoliciesExpression AllDocumentsAreMultiTenanted()
        {
            return ForAllDocuments(_ => _.TenancyStyle = TenancyStyle.Conjoined);
        }

        /// <summary>
        ///     Unless explicitly marked otherwise, all documents should
        ///     be soft-deleted
        /// </summary>
        /// <returns></returns>
        public PoliciesExpression AllDocumentsSoftDeleted()
        {
            return ForAllDocuments(_ => _.DeleteStyle = DeleteStyle.SoftDelete);
        }

        /// <summary>
        ///     Turn off the informational metadata columns
        ///     in storage like the last modified, version, and
        ///     dot net type for leaner storage
        /// </summary>
        public PoliciesExpression DisableInformationalFields()
        {
            return ForAllDocuments(x =>
            {
                x.Metadata.LastModified.Enabled = false;
                x.Metadata.DotNetType.Enabled = false;
                x.Metadata.Version.Enabled = false;
            });
        }

        /// <summary>
        ///     All document types will have optimistic concurrency checks
        /// </summary>
        /// <returns></returns>
        public PoliciesExpression AllDocumentsEnforceOptimisticConcurrency()
        {
            return ForAllDocuments(x =>
            {
                x.UseOptimisticConcurrency = true;
            });
        }
    }
}

internal class LambdaDocumentPolicy: IDocumentPolicy
{
    private readonly Action<DocumentMapping> _modify;

    public LambdaDocumentPolicy(Action<DocumentMapping> modify)
    {
        _modify = modify;
    }

    public void Apply(DocumentMapping mapping)
    {
        _modify(mapping);
    }
}

public interface IReadOnlyAdvancedOptions
{
    /// <summary>
    ///     Sets Enum values stored as either integers or strings for DuplicatedField.
    /// </summary>
    EnumStorage DuplicatedFieldEnumStorage { get; }

    /// <summary>
    ///     Global default parameters for Hilo sequences within the DocumentStore. Can be overridden per document
    ///     type as well
    /// </summary>
    IReadOnlyHiloSettings HiloSequenceDefaults { get; }

    /// <summary>
    ///     Option to enable or disable usage of default tenant when using multi-tenanted documents
    /// </summary>
    bool DefaultTenantUsageEnabled { get; }
}

public class AdvancedOptions: IReadOnlyAdvancedOptions
{
    private readonly StoreOptions _storeOptions;
    private EnumStorage? _duplicatedFieldEnumStorage;

    internal AdvancedOptions(StoreOptions storeOptions)
    {
        _storeOptions = storeOptions;

        // Making the DDL generation be transactional can cause runtime errors.
        // Make the user opt into this
        Migrator.IsTransactional = false;
    }

    /// <summary>
    /// Register configurations to the ISerializer that will be applied at the last
    /// second to the application's serializer settings. This was meant for Marten
    /// add ons
    /// </summary>
    /// <param name="configure"></param>
    public void ModifySerializer(Action<ISerializer> configure)
    {
        // Apply it immediately...
        configure(_storeOptions.Serializer());

        _storeOptions.SerializationConfigurations.Add(configure);
    }

    /// <summary>
    ///     Global default parameters for Hilo sequences within the DocumentStore. Can be overridden per document
    ///     type as well
    /// </summary>
    public HiloSettings HiloSequenceDefaults { get; } = new();

    /// <summary>
    ///     Allows you to modify how the DDL for document tables and upsert functions is
    ///     written
    /// </summary>
    public PostgresqlMigrator Migrator { get; } = new();

    /// <summary>
    ///     Decides if case sensitive object names should be used while applying changes to database.
    /// </summary>
    public bool UseCaseSensitiveQualifiedNamesWhenApplyingChanges
    {
        get => PostgresqlProvider.Instance.UseCaseSensitiveQualifiedNames;
        set => PostgresqlProvider.Instance.UseCaseSensitiveQualifiedNames = value;
    }

    /// <summary>
    ///     Decides if `timestamp without time zone` database type should be used for `DateTime` DuplicatedField.
    /// </summary>
    public bool DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime { get; set; } = true;

    /// <summary>
    ///     Sets Enum values stored as either integers or strings for DuplicatedField.
    /// </summary>
    public EnumStorage DuplicatedFieldEnumStorage
    {
        get => _duplicatedFieldEnumStorage ?? _storeOptions.EnumStorage;
        set => _duplicatedFieldEnumStorage = value;
    }

    IReadOnlyHiloSettings IReadOnlyAdvancedOptions.HiloSequenceDefaults => HiloSequenceDefaults;


    /// <summary>
    ///     Option to enable or disable usage of default tenant when using multi-tenanted documents
    /// </summary>
    public bool DefaultTenantUsageEnabled { get; set; } = true;
}
