#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Schema.Identity.Sequences;
using Marten.Schema.Indexing.Unique;
using Marten.Storage;
using Marten.Storage.Metadata;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using FindMembers = Marten.Linq.Parsing.FindMembers;

namespace Marten;

internal interface IDocumentMappingBuilder
{
    Type DocumentType { get; }
    DocumentMapping Build(StoreOptions options);

    void Include(IDocumentMappingBuilder include);
}

internal class DocumentMappingBuilder<T>: IDocumentMappingBuilder
{
    private readonly IList<Action<DocumentMapping<T>>> _alterations
        = new List<Action<DocumentMapping<T>>>();

    internal Action<DocumentMapping<T>> AlterFirst
    {
        set => _alterations.Insert(0, value);
    }

    internal Action<DocumentMapping<T>> Alter
    {
        set => _alterations.Add(value);
    }

    public DocumentMapping Build(StoreOptions options)
    {
        var mapping = new DocumentMapping<T>(options);
        foreach (var alteration in _alterations) alteration(mapping);

        return mapping;
    }

    public Type DocumentType => typeof(T);

    public void Include(IDocumentMappingBuilder include)
    {
        _alterations.AddRange(include.As<DocumentMappingBuilder<T>>()._alterations);
    }
}

/// <summary>
///     Used to customize or optimize the storage and retrieval of document types
/// </summary>
public class MartenRegistry
{
    private readonly StoreOptions _storeOptions;

    internal MartenRegistry(StoreOptions storeOptions)
    {
        _storeOptions = storeOptions;
    }

    protected MartenRegistry(): this(new StoreOptions())
    {
    }

    /// <summary>
    ///     Include the declarations from another MartenRegistry type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public void Include<T>() where T : MartenRegistry, new()
    {
        Include(new T());
    }

    /// <summary>
    ///     Include the declarations from another MartenRegistry object
    /// </summary>
    /// <param name="registry"></param>
    public void Include(MartenRegistry registry)
    {
        _storeOptions.Storage.IncludeDocumentMappingBuilders(registry._storeOptions.Storage);
    }

    /// <summary>
    ///     Configure a single document type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public DocumentMappingExpression<T> For<T>()
    {
        return new DocumentMappingExpression<T>(_storeOptions.Storage.BuilderFor<T>());
    }

    public class DocumentMappingExpression<T>
    {
        private readonly DocumentMappingBuilder<T> _builder;

        internal DocumentMappingExpression(DocumentMappingBuilder<T> builder)
        {
            _builder = builder;
        }

        /// <summary>
        ///     Direct the schema migration detection to ignore the presence of the
        ///     named index on the document storage table
        /// </summary>
        /// <param name="indexName"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> IgnoreIndex(string indexName)
        {
            _builder.Alter = m => m.IgnoreIndex(indexName);
            return this;
        }

        /// <summary>
        ///     Specify the property searching mechanism for this document type. The default is
        ///     JSON_Locator_Only
        /// </summary>
        /// <param name="searching"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> PropertySearching(PropertySearching searching)
        {
            _builder.Alter = m => m.PropertySearching = searching;
            return this;
        }

        /// <summary>
        ///     Override the Postgresql schema alias for this document type in order
        ///     to disambiguate similarly named document types. The default is just
        ///     the document type name to lower case.
        /// </summary>
        /// <param name="alias"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> DocumentAlias(string alias)
        {
            _builder.AlterFirst = m => m.Alias = alias;
            return this;
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
        /// <param name="dbType">Optional, overrides the Npgsql DbType for any parameter usage of this property</param>
        /// <returns></returns>
        public DocumentMappingExpression<T> Duplicate(Expression<Func<T, object>> expression, string? pgType = null,
            NpgsqlDbType? dbType = null, Action<DocumentIndex>? configure = null, bool notNull = false)
        {
            _builder.Alter = mapping =>
            {
                mapping.Duplicate(expression, pgType, dbType, configure, notNull);
            };
            return this;
        }

        /// <summary>
        ///     Creates a computed index on this data member within the JSON data storage
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> Index(Expression<Func<T, object>> expression,
            Action<ComputedIndex>? configure = null)
        {
            _builder.Alter = m => m.Index(expression, configure);

            return this;
        }

        /// <summary>
        ///     Creates a computed index on this data member within the JSON data storage
        /// </summary>
        /// <param name="expressions"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> Index(IReadOnlyCollection<Expression<Func<T, object>>> expressions,
            Action<ComputedIndex>? configure = null)
        {
            _builder.Alter = m => m.Index(expressions, configure);

            return this;
        }

        /// <summary>
        ///     Creates a unique index on this data member within the JSON data storage
        /// </summary>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> UniqueIndex(params Expression<Func<T, object>>[] expressions)
        {
            _builder.Alter = m => m.UniqueIndex(UniqueIndexType.Computed, null, expressions);

            return this;
        }

        /// <summary>
        ///     Creates a unique index on this data member within the JSON data storage
        /// </summary>
        /// <param name="indexName">Name of the index</param>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> UniqueIndex(string indexName,
            params Expression<Func<T, object>>[] expressions)
        {
            _builder.Alter = m => m.UniqueIndex(UniqueIndexType.Computed, indexName, expressions);

            return this;
        }

        /// <summary>
        ///     Creates a unique index on this data member within the JSON data storage
        /// </summary>
        /// <param name="indexType">Type of the index</param>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> UniqueIndex(UniqueIndexType indexType,
            params Expression<Func<T, object>>[] expressions)
        {
            _builder.Alter = m => m.UniqueIndex(indexType, null, expressions);

            return this;
        }

        /// <summary>
        ///     Creates a unique index on this data member within the JSON data storage
        /// </summary>
        /// <param name="indexType">Type of the index</param>
        /// <param name="indexName">Name of the index</param>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> UniqueIndex(UniqueIndexType indexType, string indexName,
            params Expression<Func<T, object>>[] expressions)
        {
            _builder.Alter = m => m.UniqueIndex(indexType, indexName, expressions);

            return this;
        }

        /// <summary>
        ///     Creates a unique index on this data member within the JSON data storage
        /// </summary>
        /// <param name="indexType">Type of the index</param>
        /// <param name="indexTenancyStyle">Style of tenancy</param>
        /// <param name="indexName">Name of the index</param>
        /// <param name="tenancyScope">Whether the unique index applies on a per tenant basis</param>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> UniqueIndex(UniqueIndexType indexType, string indexName,
            TenancyScope tenancyScope = TenancyScope.Global, params Expression<Func<T, object>>[] expressions)
        {
            _builder.Alter = m => m.UniqueIndex(indexType, indexName, tenancyScope, expressions);

            return this;
        }

        /// <summary>
        ///     Creates an index on the predefined Last Modified column
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> IndexLastModified(Action<DocumentIndex>? configure = null)
        {
            _builder.Alter = m => m.AddLastModifiedIndex(configure);

            return this;
        }

        /// <summary>
        ///     Creates an index on the predefined Created timestamp column
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> IndexCreatedAt(Action<DocumentIndex>? configure = null)
        {
            _builder.Alter = m => m.AddCreatedAtIndex(configure);

            return this;
        }

        /// <summary>
        ///     Create a full text index
        /// </summary>
        /// <param name="regConfig"></param>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> FullTextIndex(string regConfig = Schema.FullTextIndex.DefaultRegConfig,
            Action<FullTextIndex>? configure = null)
        {
            _builder.Alter = m => m.AddFullTextIndex(regConfig, configure);
            return this;
        }

        /// <summary>
        ///     Create a full text index
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> FullTextIndex(Action<FullTextIndex> configure)
        {
            _builder.Alter = m => m.AddFullTextIndex(Schema.FullTextIndex.DefaultRegConfig, configure);
            return this;
        }

        /// <summary>
        ///     Create a full text index against designated fields on this document
        /// </summary>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> FullTextIndex(params Expression<Func<T, object>>[] expressions)
        {
            FullTextIndex(Schema.FullTextIndex.DefaultRegConfig, expressions);
            return this;
        }

        /// <summary>
        ///     Create a full text index against designated fields on this document
        /// </summary>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> FullTextIndex(string regConfig,
            params Expression<Func<T, object>>[] expressions)
        {
            _builder.Alter = m => m.FullTextIndex(regConfig, expressions);
            return this;
        }

        /// <summary>
        ///     Create a full text index against designated fields on this document
        /// </summary>
        /// <param name="expressions"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> FullTextIndex(Action<FullTextIndex> configure,
            params Expression<Func<T, object>>[] expressions)
        {
            _builder.Alter = m =>
            {
                var index = m.FullTextIndex(Schema.FullTextIndex.DefaultRegConfig, expressions);
                configure(index);
                var temp = index;
            };
            return this;
        }

        /// <summary>
        ///     Creates an n-gram index for the field which can be used for substring based matching, similar to the trigram
        ///     extension but also generates uni-grams and bi-grams.
        /// </summary>
        /// <param name="configure">
        /// </param>
        /// <returns>
        /// </returns>
        public DocumentMappingExpression<T> NgramIndex(Action<NgramIndex> configure)
        {
            _builder.Alter = m => m.AddNgramIndex(configure);
            return this;
        }

        /// <summary>
        ///     Creates an n-gram index for the field which can be used for substring based matching, similar to the trigram
        ///     extension but also generates uni-grams and bi-grams.
        /// </summary>
        /// <param name="expression">
        /// </param>
        /// <returns>
        /// </returns>
        public DocumentMappingExpression<T> NgramIndex(Expression<Func<T, object>> expression)
        {
            _builder.Alter = m => m.NgramIndex(expression);
            return this;
        }

        /// <summary>
        ///     Creates an n-gram index for the field which can be used for substring based matching, similar to the trigram
        ///     extension but also generates uni-grams and bi-grams.
        /// </summary>
        /// <param name="configure">
        /// </param>
        /// <param name="expression">
        /// </param>
        /// <returns>
        /// </returns>
        public DocumentMappingExpression<T> NgramIndex(Action<NgramIndex> configure,
            Expression<Func<T, object>> expression)
        {
            _builder.Alter = m => m.NgramIndex(configure, expression);
            return this;
        }

        /// <summary>
        ///     Add a foreign key reference to another document type
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="foreignKeyConfiguration"></param>
        /// <param name="indexConfiguration"></param>
        /// <typeparam name="TReference"></typeparam>
        /// <returns></returns>
        public DocumentMappingExpression<T> ForeignKey<TReference>(
            Expression<Func<T, object>> expression,
            Action<DocumentForeignKey>? foreignKeyConfiguration = null,
            Action<DocumentIndex>? indexConfiguration = null)
        {
            _builder.Alter = m =>
            {
                var visitor = new FindMembers();
                visitor.Visit(expression);

                var foreignKeyDefinition = m.AddForeignKey(visitor.Members.ToArray(), typeof(TReference));
                foreignKeyConfiguration?.Invoke(foreignKeyDefinition);

                var indexDefinition = m.AddIndex(foreignKeyDefinition.ColumnNames[0]);
                indexConfiguration?.Invoke(indexDefinition);
            };

            return this;
        }

        /// <summary>
        ///     Create a foreign key against the designated member of the document
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="schemaName"></param>
        /// <param name="tableName"></param>
        /// <param name="columnName"></param>
        /// <param name="foreignKeyConfiguration"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> ForeignKey(Expression<Func<T, object>> expression, string schemaName,
            string tableName, string columnName,
            Action<ForeignKey>? foreignKeyConfiguration = null)
        {
            _builder.Alter = m =>
            {
                var members = FindMembers.Determine(expression);

                var duplicateField = m.DuplicateField(members);

                var foreignKey =
                    new ForeignKey($"{m.TableName.Name}_{duplicateField.ColumnName}_fkey")
                    {
                        LinkedTable = new PostgresqlObjectName(schemaName ?? m.DatabaseSchemaName, tableName),
                        ColumnNames = new[] { duplicateField.ColumnName },
                        LinkedNames = new[] { columnName }
                    };


                foreignKeyConfiguration?.Invoke(foreignKey);
                m.ForeignKeys.Add(foreignKey);
            };

            return this;
        }

        /// <summary>
        ///     Overrides the Hilo sequence increment and "maximum low" number for document types that
        ///     use numeric id's and the Hilo Id assignment
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> HiloSettings(HiloSettings settings)
        {
            _builder.Alter = mapping => mapping.HiloSettings = settings;
            return this;
        }

        /// <summary>
        ///     Overrides the database schema name used to store the documents.
        /// </summary>
        public DocumentMappingExpression<T> DatabaseSchemaName(string databaseSchemaName)
        {
            _builder.Alter = mapping => mapping.DatabaseSchemaName = databaseSchemaName;
            return this;
        }

        /// <summary>
        ///     Overrides the stragtegy used for id generation.
        /// </summary>
        public DocumentMappingExpression<T> IdStrategy(IIdGeneration idStrategy)
        {
            _builder.Alter = mapping => mapping.IdStrategy = idStrategy;
            return this;
        }

        /// <summary>
        ///     Explicitly choose the identity member for this document type
        /// </summary>
        /// <param name="member"></param>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        public DocumentMappingExpression<T> Identity(Expression<Func<T, object>> member)
        {
            _builder.Alter = mapping =>
            {
                var members = FindMembers.Determine(member);
                if (members.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"The expression {member} is not valid as an id column in Marten");
                }

                mapping.IdMember = members.Single();
            };

            return this;
        }

        /// <summary>
        ///     Adds a Postgresql Gin index to the JSONB data column for this document type. Leads to faster
        ///     querying, but does add overhead to storage and database writes
        /// </summary>
        /// <param name="configureIndex"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> GinIndexJsonData(Action<DocumentIndex>? configureIndex = null)
        {
            _builder.Alter = mapping =>
            {
                var index = mapping.AddGinIndexToData();

                configureIndex?.Invoke(index);
            };

            return this;
        }

        /// <summary>
        ///     Programmatically directs Marten to map this type to a hierarchy of types
        /// </summary>
        /// <param name="subclassType"></param>
        /// <param name="alias"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> AddSubClass(Type subclassType, string? alias = null)
        {
            _builder.Alter = mapping => mapping.SubClasses.Add(subclassType, alias);
            return this;
        }

        /// <summary>
        ///     Programmatically directs Marten to map all the subclasses of <cref name="T" /> to a hierarchy of types
        /// </summary>
        /// <param name="allSubclassTypes">
        ///     All the subclass types of <cref name="T" /> that you wish to map.
        ///     You can use either params of <see cref="Type" /> or <see cref="MappedType" /> or a mix, since Type can implicitly
        ///     convert to MappedType (without an alias)
        /// </param>
        /// <returns></returns>
        public DocumentMappingExpression<T> AddSubClassHierarchy(params MappedType[] allSubclassTypes)
        {
            _builder.Alter = m => m.SubClasses.AddHierarchy(allSubclassTypes);
            return this;
        }

        /// <summary>
        ///     Programmatically directs Marten to map all the subclasses of <cref name="T" /> to a hierarchy of types.
        ///     <c>Unadvised in projects with many types.</c>
        /// </summary>
        /// <returns></returns>
        public DocumentMappingExpression<T> AddSubClassHierarchy()
        {
            _builder.Alter = m => m.SubClasses.AddHierarchy();

            return this;
        }

        /// <summary>
        ///     Add a sub class type to this document type so that Marten will store that document in the parent
        ///     table storage
        /// </summary>
        /// <param name="alias"></param>
        /// <typeparam name="TSubclass"></typeparam>
        /// <returns></returns>
        public DocumentMappingExpression<T> AddSubClass<TSubclass>(string? alias = null) where TSubclass : T
        {
            return AddSubClass(typeof(TSubclass), alias);
        }

        /// <summary>
        ///     Directs Marten to use the optimistic versioning checks upon updates
        ///     to this document type
        /// </summary>
        /// <returns></returns>
        public DocumentMappingExpression<T> UseOptimisticConcurrency(bool enabled)
        {
            _builder.Alter = m =>
            {
                m.UseOptimisticConcurrency = enabled;
                if (enabled)
                {
                    m.Metadata.Version.Enabled = true;
                }
            };
            return this;
        }

        /// <summary>
        ///     Directs Marten to apply "soft deletes" to this document type
        /// </summary>
        /// <returns></returns>
        public DocumentMappingExpression<T> SoftDeleted()
        {
            _builder.Alter = m => m.DeleteStyle = DeleteStyle.SoftDelete;
            return this;
        }

        /// <summary>
        ///     Mark this document type as soft-deleted, with an index on the is_deleted column
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> SoftDeletedWithIndex(Action<DocumentIndex>? configure = null)
        {
            SoftDeleted();
            _builder.Alter = m => m.AddDeletedAtIndex(configure);

            return this;
        }

        /// <summary>
        ///     Direct this document type's DDL to be created with the named template
        /// </summary>
        /// <param name="templateName"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> DdlTemplate(string templateName)
        {
            _builder.Alter = m => m.DdlTemplate = templateName;
            return this;
        }

        /// <summary>
        ///     Marks just this document type as being stored with conjoined multi-tenancy
        /// </summary>
        /// <returns></returns>
        public DocumentMappingExpression<T> MultiTenanted()
        {
            _builder.Alter = m => m.TenancyStyle = TenancyStyle.Conjoined;
            return this;
        }

        /// <summary>
        ///     Marks just this document type as being stored with single tenancy style
        /// </summary>
        /// <returns></returns>
        public DocumentMappingExpression<T> SingleTenanted()
        {
            _builder.Alter = m => m.TenancyStyle = TenancyStyle.Single;
            return this;
        }

        /// <summary>
        ///     Opt into the identity key generation strategy
        /// </summary>
        /// <returns></returns>
        public DocumentMappingExpression<T> UseIdentityKey()
        {
            _builder.Alter = m => m.IdStrategy = new IdentityKeyGeneration(m, m.HiloSettings);
            return this;
        }

        /// <summary>
        ///     Configure the metadata storage for only this document type
        /// </summary>
        /// <param name="configure"></param>
        /// <returns></returns>
        public DocumentMappingExpression<T> Metadata(Action<MetadataConfig> configure)
        {
            var metadata = new MetadataConfig(this);
            configure(metadata);

            return this;
        }

        public class MetadataConfig
        {
            private readonly DocumentMappingExpression<T> _parent;

            public MetadataConfig(DocumentMappingExpression<T> parent)
            {
                _parent = parent;
            }

            /// <summary>
            ///     The current version of this document in the database
            /// </summary>
            public Column<Guid> Version => new(_parent, m => m.Version);

            /// <summary>
            ///     Timestamp of the last time this document was modified
            /// </summary>
            public Column<DateTimeOffset> LastModified =>
                new(_parent, m => m.LastModified);

            /// <summary>
            ///     Optional metadata for the timestamp of when this document was created
            /// </summary>
            public Column<string> CreatedAt => new(_parent, m => m.CreatedAt);

            /// <summary>
            ///     The stored tenant id of this document
            /// </summary>
            public Column<string> TenantId => new(_parent, m => m.TenantId);

            /// <summary>
            ///     If soft-deleted, whether or not the document is marked as deleted
            /// </summary>
            public Column<bool> IsSoftDeleted => new(_parent, m => m.IsSoftDeleted);

            /// <summary>
            ///     If soft-deleted, the time at which the document was marked as deleted
            /// </summary>
            public Column<DateTimeOffset?> SoftDeletedAt =>
                new(_parent, m => m.SoftDeletedAt);

            /// <summary>
            ///     If the document is part of a type hierarchy, this designates
            ///     Marten's internal name for the sub type
            /// </summary>
            public Column<string> DocumentType => new(_parent, m => m.DocumentType);

            /// <summary>
            ///     The full name of the .Net type that was persisted
            /// </summary>
            public Column<string> DotNetType => new(_parent, m => m.DotNetType);

            /// <summary>
            ///     Optional metadata describing the correlation id for a
            ///     unit of work
            /// </summary>
            public Column<string> CorrelationId => new(_parent, m => m.CorrelationId);

            /// <summary>
            ///     Optional metadata describing the correlation id for a
            ///     unit of work
            /// </summary>
            public Column<string> CausationId => new(_parent, m => m.CausationId);

            /// <summary>
            ///     Optional metadata describing the user name or
            ///     process name for this unit of work
            /// </summary>
            public Column<string> LastModifiedBy => new(_parent, m => m.LastModifiedBy);

            /// <summary>
            ///     Optional, user defined headers
            /// </summary>
            public Column<Dictionary<string, object>> Headers => new(_parent, m => m.Headers);

            /// <summary>
            ///     Turn off the informational metadata columns
            ///     in storage like the last modified, version, and
            ///     dot net type for leaner storage
            /// </summary>
            public void DisableInformationalFields()
            {
                LastModified.Enabled = false;
                DotNetType.Enabled = false;
                Version.Enabled = false;
            }

            public class Column<TProperty>
            {
                private readonly DocumentMappingExpression<T> _parent;
                private readonly Func<DocumentMetadataCollection, MetadataColumn> _source;

                internal Column(DocumentMappingExpression<T> parent,
                    Func<DocumentMetadataCollection, MetadataColumn> source)
                {
                    _parent = parent;
                    _source = source;
                }

                /// <summary>
                ///     Is the metadata field enabled. Note that this can not
                ///     be overridden in some cases like the "version" column
                ///     when a document uses optimistic versioning
                /// </summary>
                public bool Enabled
                {
                    set => _parent._builder.Alter = m => _source(m.Metadata).Enabled = value;
                }

                /// <summary>
                ///     Map this metadata information to the designated Field or Property
                ///     on the document type. This will also enable the tracking column
                ///     in the underlying database table
                /// </summary>
                /// <param name="memberExpression"></param>
                public void MapTo(Expression<Func<T, TProperty>> memberExpression)
                {
                    var member = FindMembers.Determine(memberExpression).Single();
                    _parent._builder.Alter = m =>
                    {
                        var metadataColumn = _source(m.Metadata);
                        metadataColumn.Enabled = true;
                        metadataColumn.Member = member;
                    };
                }
            }
        }
    }
}

/// <summary>
///     Configures hierarchical type mapping to its parent
/// </summary>
public class MappedType
{
    public MappedType(Type type, string? alias = null)
    {
        Type = type;
        Alias = alias;
    }

    /// <summary>
    ///     The .Net Type
    /// </summary>
    public Type Type { get; set; }

    /// <summary>
    ///     String alias that will be used to persist or load the documents
    ///     from the underlying database
    /// </summary>
    public string? Alias { get; set; }

    public static implicit operator MappedType(Type type)
    {
        return new MappedType(type);
    }
}
