using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Exceptions;
using Marten.Linq.Members;
using Marten.Linq.Members.ValueCollections;
using Marten.Linq.Parsing;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Indexing.FullText;
using Marten.Schema.Indexing.Unique;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Weasel.Postgresql.Tables.Indexes;

namespace Marten.Schema;

public interface IDocumentType
{
    IDocumentType Root { get; }

    Type DocumentType { get; }

    Type IdType { get; }
    DbObjectName TableName { get; }

    DocumentMetadataCollection Metadata { get; }
    bool UseOptimisticConcurrency { get; }
    IList<IndexDefinition> Indexes { get; }
    IList<ForeignKey> ForeignKeys { get; }
    SubClasses SubClasses { get; }
    DbObjectName UpsertFunction { get; }
    DbObjectName InsertFunction { get; }
    DbObjectName UpdateFunction { get; }
    DbObjectName OverwriteFunction { get; }
    string DatabaseSchemaName { get; set; }
    EnumStorage EnumStorage { get; }
    Casing Casing { get; }
    string Alias { get; set; }
    IIdGeneration IdStrategy { get; }
    MemberInfo IdMember { get; }
    bool StructuralTyped { get; }
    string DdlTemplate { get; }
    IReadOnlyHiloSettings HiloSettings { get; }
    TenancyStyle TenancyStyle { get; }
    IReadOnlyList<DuplicatedField> DuplicatedFields { get; }
    bool IsHierarchy();
    IEnumerable<DocumentIndex> IndexesFor(string column);
    string AliasFor(Type subclassType);
    Type TypeFor(string alias);
}

public class DocumentMapping: IDocumentMapping, IDocumentType
{
    internal static bool IsValidIdentityType(Type identityType)
    {
        if (identityType == null) return false;

        if (identityType.IsGenericType && identityType.IsNullable())
        {
            identityType = identityType.GetGenericArguments()[0];
        }

        return identityType.IsOneOf(ValidIdTypes) ||
               ValueTypeIdGeneration.IsCandidate(identityType, out var generation) ||
               FSharpDiscriminatedUnionIdGeneration.IsCandidate(identityType,
                   out var fSharpDiscriminatedUnionIdGeneration);
    }

    private static readonly Regex _aliasSanitizer = new("<|>", RegexOptions.Compiled);
    internal static readonly Type[] ValidIdTypes = { typeof(int), typeof(Guid), typeof(long), typeof(string) };
    private readonly List<DuplicatedField> _duplicates = new();
    private readonly Lazy<DocumentSchema> _schema;

    private string _alias;
    private string _databaseSchemaName;

    private HiloSettings _hiloSettings;
    private MemberInfo _idMember;

    public DocumentMapping(Type documentType, StoreOptions storeOptions)
    {
        if (documentType.IsSimple())
        {
            throw new ArgumentOutOfRangeException(nameof(documentType),
                "This type cannot be used as a Marten document");
        }

        StoreOptions = storeOptions ?? throw new ArgumentNullException(nameof(storeOptions));

        DocumentType = documentType ?? throw new ArgumentNullException(nameof(documentType));
        Alias = defaultDocumentAliasName(documentType);

        QueryMembers = new DocumentQueryableMemberCollection(this, StoreOptions);
        IdMember = FindIdMember(documentType);

        Metadata = new DocumentMetadataCollection(this);

        SubClasses = new SubClasses(this, storeOptions);

        StoreOptions.applyPolicies(this);

        applyAnyMartenAttributes(documentType);

        _schema = new Lazy<DocumentSchema>(() => new DocumentSchema(this));
    }

    public DocumentCodeGen? CodeGen { get; private set; }

    internal DocumentQueryableMemberCollection QueryMembers { get; }

    public IList<string> IgnoredIndexes { get; } = new List<string>();

    internal StoreOptions StoreOptions { get; }

    internal DocumentSchema Schema => _schema.Value;

    /// <summary>
    /// This is a workaround for the quick append + inline projection
    /// issue
    /// </summary>
    public bool UseVersionFromMatchingStream { get; set; }

    public HiloSettings HiloSettings
    {
        get => _hiloSettings;
        set
        {
            if (IdStrategy is HiloIdGeneration)
            {
                IdStrategy = new HiloIdGeneration(DocumentType, value);
                _hiloSettings = value;
            }
            else
            {
                throw new InvalidOperationException(
                    $"DocumentMapping for {DocumentType.FullName} is using {IdStrategy.GetType().FullName} as its Id strategy so cannot override Hilo sequence configuration");
            }
        }
    }

    // TODO: This should be smarter, maybe nullable option for Schema or some other base type
    internal bool SkipSchemaGeneration { get; set; }

    public MemberInfo IdMember
    {
        get => _idMember;
        set
        {
            if (value != null && !IsValidIdentityType(value.GetMemberType()))
            {
                throw new InvalidDocumentException(
                    $"Id members must be an int, long, Guid, string or a valid strong typed id, but found {value.GetMemberType().FullNameInCode()}");
            }

            _idMember = value;

            if (_idMember != null)
            {
                IdStrategy = StoreOptions.DetermineIdStrategy(DocumentType, IdMember);
                CodeGen = new DocumentCodeGen(this);
            }


        }
    }

    public Type IdType => IdMember?.GetMemberType();

    IDocumentMapping IDocumentMapping.Root => this;

    public Type DocumentType { get; }

    public virtual DbObjectName TableName =>
        new PostgresqlObjectName(DatabaseSchemaName, $"{SchemaConstants.TablePrefix}{_alias}");

    public DocumentMetadataCollection Metadata { get; }

    public bool UseOptimisticConcurrency { get; set; }

    public bool UseNumericRevisions { get; set; }

    public IList<IndexDefinition> Indexes { get; } = new List<IndexDefinition>();

    public IList<ForeignKey> ForeignKeys { get; } = new List<ForeignKey>();

    public SubClasses SubClasses { get; }

    public DbObjectName UpsertFunction =>
        new PostgresqlObjectName(DatabaseSchemaName, $"{SchemaConstants.UpsertPrefix}{_alias}");

    public DbObjectName InsertFunction =>
        new PostgresqlObjectName(DatabaseSchemaName, $"{SchemaConstants.InsertPrefix}{_alias}");

    public DbObjectName UpdateFunction =>
        new PostgresqlObjectName(DatabaseSchemaName, $"{SchemaConstants.UpdatePrefix}{_alias}");

    public DbObjectName OverwriteFunction =>
        new PostgresqlObjectName(DatabaseSchemaName, $"{SchemaConstants.OverwritePrefix}{_alias}");

    public string DatabaseSchemaName
    {
        get => _databaseSchemaName ?? StoreOptions.DatabaseSchemaName;
        set => _databaseSchemaName = value.ToLowerInvariant();
    }

    public EnumStorage EnumStorage => StoreOptions.EnumStorage;

    public Casing Casing => StoreOptions.Serializer().Casing;

    public string Alias
    {
        get => _alias;
        set
        {
            if (value.IsEmpty())
            {
                throw new ArgumentNullException(nameof(value));
            }

            _alias = value.ToLower();
        }
    }

    public PropertySearching PropertySearching { get; set; } = PropertySearching.JSON_Locator_Only;
    public DeleteStyle DeleteStyle { get; set; } = DeleteStyle.Remove;

    public IIdGeneration IdStrategy { get; set; }

    public bool StructuralTyped { get; set; }

    public string DdlTemplate { get; set; }

    IReadOnlyHiloSettings IDocumentType.HiloSettings { get; }

    public TenancyStyle TenancyStyle { get; set; } = TenancyStyle.Single;

    IDocumentType IDocumentType.Root => this;

    public IReadOnlyList<DuplicatedField> DuplicatedFields => _duplicates;

    public bool IsHierarchy()
    {
        return SubClasses.Any() || DocumentType.GetTypeInfo().IsAbstract || DocumentType.GetTypeInfo().IsInterface;
    }

    public IEnumerable<DocumentIndex> IndexesFor(string column)
    {
        return Indexes.OfType<DocumentIndex>().Where(x => x.Columns.Contains(column));
    }

    public string AliasFor(Type subclassType)
    {
        if (subclassType == DocumentType)
        {
            return SchemaConstants.BaseAlias;
        }

        var type = SubClasses.FirstOrDefault(x => x.DocumentType == subclassType);
        if (type == null)
        {
            throw new ArgumentOutOfRangeException(
                $"Unknown subclass type '{subclassType.FullName}' for Document Hierarchy {DocumentType.FullName}");
        }

        return type.Alias;
    }

    // This method is used in generated code, so please don't delete this!!!!
    public Type TypeFor(string alias)
    {
        if (alias == SchemaConstants.BaseAlias)
        {
            return DocumentType;
        }

        var subClassMapping = SubClasses.FirstOrDefault(x => x.Alias.EqualsIgnoreCase(alias));
        if (subClassMapping == null)
        {
            throw new ArgumentOutOfRangeException(nameof(alias),
                $"No subclass in the hierarchy '{DocumentType.FullName}' matches the alias '{alias}'");
        }

        return subClassMapping.DocumentType;
    }

    /// <summary>
    ///     This directs the schema migration functionality to ignore the presence of this named index
    ///     on the document storage table
    /// </summary>
    /// <param name="indexName"></param>
    public void IgnoreIndex(string indexName)
    {
        IgnoredIndexes.Add(indexName);
    }

    private void applyAnyMartenAttributes(Type documentType)
    {
        documentType.ForAttribute<MartenAttribute>(att => att.Modify(this));

        documentType.GetProperties()
            .Where(x => !x.HasAttribute<DuplicateFieldAttribute>() &&
                        PostgresqlProvider.Instance.HasTypeMapping(x.PropertyType))
            .Each(prop => { prop.ForAttribute<MartenAttribute>(att => att.Modify(this, prop)); });

        documentType.GetFields()
            .Where(x => !x.HasAttribute<DuplicateFieldAttribute>() &&
                        PostgresqlProvider.Instance.HasTypeMapping(x.FieldType))
            .Each(fieldInfo => { fieldInfo.ForAttribute<MartenAttribute>(att => att.Modify(this, fieldInfo)); });

        // DuplicateFieldAttribute does not require TypeMappings check
        documentType.GetProperties()
            .Where(x => x.HasAttribute<DuplicateFieldAttribute>())
            .Each(prop => { prop.ForAttribute<DuplicateFieldAttribute>(att => att.Modify(this, prop)); });

        documentType.GetFields()
            .Where(x => x.HasAttribute<DuplicateFieldAttribute>())
            .Each(fieldInfo =>
            {
                fieldInfo.ForAttribute<DuplicateFieldAttribute>(att => att.Modify(this, fieldInfo));
            });
    }

    /// <summary>
    ///     Access to all other document types that are linked to by foreign keys
    ///     from this document type
    /// </summary>
    /// <returns></returns>
    public IEnumerable<Type> ReferencedTypes()
    {
        return ForeignKeys.OfType<DocumentForeignKey>().Select(x => x.ReferenceDocumentType)
            .Where(x => x != DocumentType);
    }

    public static DocumentMapping<T> For<T>(string databaseSchemaName = SchemaConstants.DefaultSchema)
    {
        var storeOptions = new StoreOptions { DatabaseSchemaName = databaseSchemaName };

        var documentMapping = new DocumentMapping<T>(storeOptions);
        documentMapping.CompileAndValidate();
        return documentMapping;
    }

    public static MemberInfo FindIdMember(Type documentType)
    {
        // Order of finding id member should be
        // 1) IdentityAttribute on property
        // 2) IdentityAttribute on field
        // 3) Id Property
        // 4) Id field
        var propertiesWithTypeValidForId = GetProperties(documentType)
            .Where(p => IsValidIdentityType(p.PropertyType));
        var fieldsWithTypeValidForId = documentType.GetFields()
            .Where(f => IsValidIdentityType(f.FieldType));
        return propertiesWithTypeValidForId.FirstOrDefault(x => x.HasAttribute<IdentityAttribute>())
               ?? fieldsWithTypeValidForId.FirstOrDefault(x => x.HasAttribute<IdentityAttribute>())
               ?? (MemberInfo)propertiesWithTypeValidForId
                   .FirstOrDefault(x => x.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
               ?? fieldsWithTypeValidForId
                   .FirstOrDefault(x => x.Name.Equals("id", StringComparison.OrdinalIgnoreCase));

    }

    private static PropertyInfo[] GetProperties(Type type)
    {
        return type.GetTypeInfo().IsInterface
            ? new[] { type }
                .Concat(type.GetInterfaces())
                .SelectMany(i => i.GetProperties()).ToArray()
            : type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .OrderByDescending(x => x.DeclaringType == type).ToArray();
    }

    public DocumentIndex AddGinIndexToData()
    {
        var index = AddIndex("data");
        index.ToGinWithJsonbPathOps();

        PropertySearching = PropertySearching.ContainmentOperator;

        return index;
    }

    public DocumentIndex AddLastModifiedIndex(Action<DocumentIndex> configure = null)
    {
        var index = new DocumentIndex(this, SchemaConstants.LastModifiedColumn);
        configure?.Invoke(index);
        Indexes.Add(index);

        return index;
    }

    public DocumentIndex AddCreatedAtIndex(Action<DocumentIndex> configure = null)
    {
        var index = new DocumentIndex(this, SchemaConstants.CreatedAtColumn);
        configure?.Invoke(index);
        Indexes.Add(index);

        return index;
    }

    public DocumentIndex AddDeletedAtIndex(Action<DocumentIndex> configure = null)
    {
        if (DeleteStyle != DeleteStyle.SoftDelete)
        {
            throw new InvalidOperationException(
                $"DocumentMapping for {DocumentType.FullName} is not configured to use Soft Delete");
        }

        var index = new DocumentIndex(this, SchemaConstants.DeletedAtColumn)
        {
            Predicate = SchemaConstants.DeletedColumn
        };

        configure?.Invoke(index);
        Indexes.Add(index);

        return index;
    }

    public DocumentIndex AddIndex(params string[] columns)
    {
        var existing = Indexes.OfType<DocumentIndex>().FirstOrDefault(x => x.Columns.SequenceEqual(columns));
        if (existing != null)
        {
            return existing;
        }

        var index = new DocumentIndex(this, columns);
        Indexes.Add(index);

        return index;
    }

    public IndexDefinition AddUniqueIndex(MemberInfo[][] members,
        UniqueIndexType indexType = UniqueIndexType.Computed, string indexName = null,
        IndexMethod indexMethod = IndexMethod.btree, TenancyScope tenancyScope = TenancyScope.Global)
    {
        if (indexType == UniqueIndexType.DuplicatedField)
        {
            var fields = members.Select(memberPath => DuplicateField(memberPath)).ToList();

            var index = AddIndex(fields.Select(m => m.ColumnName).ToArray());
            index.Name = indexName;
            index.Method = indexMethod;
            index.IsUnique = true;
            index.TenancyScope = tenancyScope;

            return index;
        }
        else
        {
            var index = new ComputedIndex(
                this,
                members) { Method = indexMethod, Name = indexName, IsUnique = true, TenancyScope = tenancyScope };

            var existing = Indexes.OfType<ComputedIndex>().FirstOrDefault(x => x.Name == index.Name);
            if (existing != null)
            {
                return existing;
            }

            Indexes.Add(index);

            return index;
        }
    }

    /// <summary>
    ///     Adds a full text index
    /// </summary>
    /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
    /// <param name="configure">Optional action to further configure the full text index</param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
    /// </remarks>
    public FullTextIndexDefinition AddFullTextIndex(
        string regConfig = FullTextIndexDefinition.DefaultRegConfig,
        Action<FullTextIndexDefinition> configure = null
    )
    {
        var index = FullTextIndexDefinitionFactory.From(this, regConfig);
        configure?.Invoke(index);

        return AddFullTextIndexIfDoesNotExist(index);
    }

    /// <summary>
    ///     Adds a full text index
    /// </summary>
    /// <param name="members">Document fields that should be use by full text index</param>
    /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
    /// </remarks>
    public FullTextIndexDefinition AddFullTextIndex(
        MemberInfo[][] members,
        string regConfig = FullTextIndexDefinition.DefaultRegConfig,
        string indexName = null
    )
    {
        var index = FullTextIndexDefinitionFactory.From(this, members, regConfig);
        if (indexName != null)
            index.Name = indexName;

        return AddFullTextIndexIfDoesNotExist(index);
    }

    private FullTextIndexDefinition AddFullTextIndexIfDoesNotExist(FullTextIndexDefinition index)
    {
        var existing = Indexes.OfType<FullTextIndexDefinition>().FirstOrDefault(x => x.Name == index.Name);
        if (existing != null)
        {
            return existing;
        }

        Indexes.Add(index);

        return index;
    }

    /// <summary>
    ///     Adds a full text index
    /// </summary>
    /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
    /// <param name="configure">Optional action to further configure the ngram index</param>
    public NgramIndex AddNgramIndex(Action<NgramIndex> configure = null)
    {
        var index = new NgramIndex(this);
        configure?.Invoke(index);

        return AddNgramIndexIfDoesNotExist(index);
    }

    /// <summary>
    ///     Adds a full text index
    /// </summary>
    /// <param name="members">Document fields that should be use by ngram index</param>
    public NgramIndex AddNgramIndex(MemberInfo[] members, string indexName = null)
    {
        var index = new NgramIndex(this, members) { Name = indexName };

        return AddNgramIndexIfDoesNotExist(index);
    }

    private NgramIndex AddNgramIndexIfDoesNotExist(NgramIndex index)
    {
        var existing = Indexes.OfType<NgramIndex>().FirstOrDefault(x => x.Name == index.Name);
        if (existing != null)
        {
            return existing;
        }

        Indexes.Add(index);

        return index;
    }

    public DocumentForeignKey AddForeignKey(string memberName, Type referenceType)
    {
        var member = DocumentType.GetProperty(memberName) ?? (MemberInfo)DocumentType.GetField(memberName);

        return AddForeignKey(new MemberInfo[] { member }, referenceType);
    }

    public DocumentForeignKey AddForeignKey(MemberInfo[] members, Type referenceType)
    {
        var referenceMapping =
            referenceType != DocumentType ? StoreOptions.Storage.MappingFor(referenceType) : this;

        var duplicateField = DuplicateField(members);

        var foreignKey = new DocumentForeignKey(duplicateField.ColumnName, this, referenceMapping);
        ForeignKeys.Add(foreignKey);

        return foreignKey;
    }

    private static string defaultDocumentAliasName(Type documentType)
    {
        var nameToAlias = documentType.Name;
        if (documentType.GetTypeInfo().IsGenericType)
        {
            nameToAlias = _aliasSanitizer.Replace(documentType.GetPrettyName(), string.Empty).Replace(",", "_");
        }

        var parts = new List<string> { nameToAlias.ToLower() };
        if (documentType.IsNested)
        {
            parts.Insert(0, documentType.DeclaringType.Name.ToLower());
        }

        return string.Join("_", parts);
    }

    public DuplicatedField DuplicateField(string memberName, string pgType = null, bool notNull = false)
    {
        var member = (QueryableMember)QueryMembers.MemberFor(memberName);

        var enumStorage = StoreOptions.Advanced.DuplicatedFieldEnumStorage;
        var dateTimeStorage = StoreOptions.Advanced.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime;
        var duplicate = member is ValueCollectionMember collectionMember
            ? new DuplicatedArrayField(enumStorage, collectionMember, dateTimeStorage, notNull)
            : new DuplicatedField(enumStorage, (QueryableMember)member, dateTimeStorage, notNull);

        if (pgType.IsNotEmpty())
        {
            duplicate.PgType = pgType;
        }

        QueryMembers.ReplaceMember(member.Member, duplicate);

        _duplicates.Add(duplicate);

        return duplicate;
    }

    public DuplicatedField DuplicateField(MemberInfo[] members, string pgType = null, string columnName = null,
        bool notNull = false)
    {
        var member = QueryMembers.FindMember(members[0]);
        var parent = (IHasChildrenMembers)QueryMembers;
        for (var i = 1; i < members.Length; i++)
        {
            parent = member.As<IHasChildrenMembers>();
            member = parent.FindMember(members[i]);
        }

        if (member is DuplicatedField d)
        {
            if (pgType != null) d.PgType = pgType;
            if (columnName != null) d.ColumnName = columnName;
            d.NotNull = notNull;
            return d;
        }

        if (member is not QueryableMember)
        {
            throw new ArgumentOutOfRangeException(nameof(members),
                $"{members.Select(x => x.Name).Join(".")} of type {member.MemberType.FullNameInCode()} cannot be used as a Duplicated Field by Marten");
        }

        var enumStorage = StoreOptions.Advanced.DuplicatedFieldEnumStorage;
        var dateTimeStorage = StoreOptions.Advanced.DuplicatedFieldUseTimestampWithoutTimeZoneForDateTime;
        var duplicatedField = member is ValueCollectionMember collectionMember
            ? new DuplicatedArrayField(enumStorage, collectionMember, dateTimeStorage, notNull)
            : new DuplicatedField(enumStorage, (QueryableMember)member, dateTimeStorage, notNull);

        parent.ReplaceMember(members.Last(), duplicatedField);

        if (pgType.IsNotEmpty())
        {
            duplicatedField.PgType = pgType;
        }

        if (columnName.IsNotEmpty())
        {
            duplicatedField.ColumnName = columnName;
        }

        _duplicates.Add(duplicatedField);

        return duplicatedField;
    }

    internal void CompileAndValidate()
    {
        if (IdMember == null)
        {
            throw new InvalidDocumentException(
                $"Could not determine an 'id/Id' field or property for requested document type {DocumentType.FullName}");
        }

        if (Metadata.TenantId.Member != null && TenancyStyle != TenancyStyle.Conjoined)
        {
            throw new InvalidDocumentException(
                $"Tenancy style must be set to {nameof(TenancyStyle.Conjoined)} to map tenant id metadata for {DocumentType.FullName}.");
        }

        if (Metadata.DocumentType.Member != null && !IsHierarchy())
        {
            throw new InvalidDocumentException(
                $"{DocumentType.FullName} must be part of a document hierarchy to map document type metadata.");
        }

        if ((Metadata.IsSoftDeleted.Member != null || Metadata.SoftDeletedAt.Member != null) &&
            DeleteStyle != DeleteStyle.SoftDelete)
        {
            throw new InvalidDocumentException(
                $"{DocumentType.FullName} must be configured for soft deletion to map soft deleted metadata.");
        }

        if (UseNumericRevisions && UseOptimisticConcurrency)
        {
            throw new InvalidDocumentException(
                $"{DocumentType.FullNameInCode()} cannot be configured with UseNumericRevision and UseOptimisticConcurrency. Choose one or the other");
        }

        IQueryableMember idField;
        if (IdStrategy is ValueTypeIdGeneration st)
        {
            idField = typeof(StrongTypedIdMember<,>).CloseAndBuildAs<IQueryableMember>(IdMember, st, st.OuterType,
                st.SimpleType);
        }
        else if (IdStrategy is FSharpDiscriminatedUnionIdGeneration fst)
        {
            idField = typeof(StrongTypedIdMember<,>).CloseAndBuildAs<IQueryableMember>(IdMember, fst, fst.OuterType,
                fst.SimpleType);
        }
        else
        {
            idField = new IdMember(IdMember);
        }
        QueryMembers.ReplaceMember(IdMember, idField);
    }

    public override string ToString()
    {
        return $"Storage for {DocumentType}, Table: {TableName}";
    }

    internal Type InnerIdType()
    {
        if (IdStrategy is ValueTypeIdGeneration sti) return sti.SimpleType;

        var memberType = _idMember.GetMemberType();
        return memberType.IsNullable() ? memberType.GetGenericArguments()[0] : memberType;
    }
}

public class DocumentMapping<T>: DocumentMapping
{
    public DocumentMapping(StoreOptions storeOptions): base(typeof(T), storeOptions)
    {
        var configure = typeof(T).GetMethod("ConfigureMarten", BindingFlags.Static | BindingFlags.Public);
        configure?.Invoke(null, new object[] { this });
    }

    /// <summary>
    ///     Marks a property or field on this document type as a searchable field that is also duplicated in the
    ///     database document table
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="pgType">Optional, overrides the Postgresql column type for the duplicated field</param>
    /// <param name="configure">
    ///     Optional, allows you to customize the Postgresql database index configured for the duplicated
    ///     field
    /// </param>
    /// <returns></returns>
    public void Duplicate(Expression<Func<T, object>> expression, string pgType = null, NpgsqlDbType? dbType = null,
        Action<DocumentIndex> configure = null, bool notNull = false)
    {
        var visitor = new FindMembers();
        visitor.Visit(expression);

        var duplicateField = DuplicateField(visitor.Members.ToArray(), pgType, notNull: notNull);

        if (dbType.HasValue)
        {
            duplicateField.DbType = dbType.Value;
        }

        var indexDefinition = AddIndex(duplicateField.ColumnName);
        configure?.Invoke(indexDefinition);
    }


    /// <summary>
    ///     Adds a computed index
    /// </summary>
    /// <param name="expression"></param>
    /// <param name="configure"></param>
    public void Index(Expression<Func<T, object>> expression, Action<ComputedIndex> configure = null)
    {
        if (expression.Body is NewExpression newExpression)
        {
            var members = newExpression.Arguments
                .Select(FindMembers.Determine).ToArray();

            var index = new ComputedIndex(this, members);
            configure?.Invoke(index);
            Indexes.Add(index);
            return;
        }


        Index(new[] { expression }, configure);
    }

    /// <summary>
    ///     Adds a computed index
    /// </summary>
    /// <param name="expressions"></param>
    /// <param name="configure"></param>
    public void Index(IReadOnlyCollection<Expression<Func<T, object>>> expressions,
        Action<ComputedIndex> configure = null)
    {
        var members = expressions
            .Select(FindMembers.Determine).ToArray();

        var index = new ComputedIndex(this, members);
        configure?.Invoke(index);
        Indexes.Add(index);
    }

    public void UniqueIndex(UniqueIndexType indexType, string indexName,
        params Expression<Func<T, object>>[] expressions)
    {
        UniqueIndex(indexType, indexName, TenancyScope.Global, expressions);
    }

    public void UniqueIndex(UniqueIndexType indexType, string indexName,
        TenancyScope tenancyScope = TenancyScope.Global, params Expression<Func<T, object>>[] expressions)
    {
        var members = expressions
            .Select(e =>
            {
                var visitor = new Marten.Linq.Parsing.MemberFinder();
                visitor.Visit(e);
                return visitor.Members.ToArray();
            })
            .ToArray();

        if (members.Length == 0)
        {
            throw new InvalidOperationException(
                $"Unique index on {typeof(T)} requires at least one property/field");
        }

        AddUniqueIndex(
            members,
            indexType,
            indexName,
            IndexMethod.btree,
            tenancyScope);
    }

    /// <summary>
    ///     Adds a full text index
    /// </summary>
    /// <param name="regConfig">The dictionary to used by the 'to_tsvector' function, defaults to 'english'.</param>
    /// <param name="expressions">Document fields that should be use by full text index</param>
    /// <remarks>
    ///     See: https://www.postgresql.org/docs/10/static/textsearch-controls.html#TEXTSEARCH-PARSING-DOCUMENTS
    /// </remarks>
    public FullTextIndexDefinition FullTextIndex(string regConfig, params Expression<Func<T, object>>[] expressions)
    {
        return AddFullTextIndex(
            expressions
                .Select(FindMembers.Determine)
                .ToArray(),
            regConfig);
    }

    /// <summary>
    ///     Adds an ngram index.
    /// </summary>
    /// <param name="expression">Document field that should be use by ngram index</param>
    public NgramIndex NgramIndex(Expression<Func<T, object>> expression)
    {
        var visitor = new FindMembers();
        visitor.Visit(expression);

        return AddNgramIndex(visitor.Members.ToArray());
    }

    /// <summary>
    ///     Adds a full text index with default region config set to 'english'
    /// </summary>
    /// <param name="expressions">Document fields that should be use by full text index</param>
    public NgramIndex NgramIndex(Action<NgramIndex> configure, Expression<Func<T, object>> expression)
    {
        var index = NgramIndex(expression);
        configure(index);
        return index;
    }
}

public class DocumentCodeGen
{
    public DocumentCodeGen(DocumentMapping mapping)
    {
        AccessId = mapping.IdMember.GetRawMemberType().IsNullable()
            ? $"{mapping.IdMember.Name}.Value"
            : mapping.IdMember.Name;

        ParameterValue = mapping.IdMember.Name;
        if (mapping.IdStrategy is ValueTypeIdGeneration st)
        {
            ParameterValue = st.ParameterValue(mapping);
        }

        if (mapping.IdStrategy is FSharpDiscriminatedUnionIdGeneration fst)
        {
            ParameterValue = fst.ParameterValue(mapping);
        }
    }

    public string AccessId { get; }
    public string ParameterValue { get; }
}
