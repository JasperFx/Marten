#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal.CodeGeneration;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Storage;
using Marten.Storage.Metadata;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Internal.Storage;


internal interface IHaveMetadataColumns
{
    MetadataColumn[] MetadataColumns();
}

public abstract class DocumentStorage<T, TId>: IDocumentStorage<T, TId>, IHaveMetadataColumns where T : notnull where TId : notnull
{
    private readonly NpgsqlDbType _idType;

    protected readonly string _loadArraySql;
    protected readonly string _loaderSql;
    protected readonly DocumentMapping _mapping;
    private readonly string _selectClause;
    private readonly string[] _selectFields;
    private ISqlFragment? _defaultWhere;
    protected Action<T, TId> _setter;
    protected Action<T, string> _setFromString = (_, _) => throw new NotSupportedException();
    protected Action<T, Guid> _setFromGuid = (_, _) => throw new NotSupportedException();


    private readonly DocumentMapping _document;

    public DocumentStorage(StorageStyle storageStyle, DocumentMapping document)
    {
        _mapping = document;

        TableName = document.TableName;

        determineDefaultWhereFragment();

        _idType = PostgresqlProvider.Instance.ToParameterType(typeof(TId));

        var table = _mapping.Schema.Table;

        DuplicatedFields = _mapping.DuplicatedFields;

        _selectFields = table.SelectColumns(storageStyle).Select(x => $"d.{x.Name}").ToArray();
        var fieldSelector = _selectFields.Join(", ");
        _selectClause = $"select {fieldSelector} from {document.TableName.QualifiedName} as d";

        _document = document;

        _loaderSql =
            $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = $1";

        _loadArraySql =
            $"select {fieldSelector} from {document.TableName.QualifiedName} as d where id = ANY($1)";

        if (document.TenancyStyle == TenancyStyle.Conjoined)
        {
            _loaderSql += $" and d.{TenantIdColumn.Name} = $2";
            _loadArraySql += $" and d.{TenantIdColumn.Name} = $2";
        }

        UseOptimisticConcurrency = document.UseOptimisticConcurrency;
        UseNumericRevisions = document.UseNumericRevisions;

        _setter = LambdaBuilder.Setter<T, TId>(document.IdMember)!;
        if (typeof(TId) == typeof(Guid))
        {
            _setFromGuid = _setter.As<Action<T, Guid>>();
        }
        else if (typeof(TId) == typeof(string))
        {
            _setFromString = _setter.As<Action<T, string>>();
        }
        else if (document.IdStrategy is ValueTypeIdGeneration valueType)
        {
            if (valueType.SimpleType == typeof(Guid))
            {
                var converter = valueType.CreateConverter<TId, Guid>();
                _setFromGuid = (doc, guid) => _setter(doc, converter(guid));
            }
            else if (valueType.SimpleType == typeof(string))
            {
                var converter = valueType.CreateConverter<TId, string>();
                _setFromString = (doc, s) => _setter(doc, converter(s));
            }
        }

        DeleteFragment = _mapping.DeleteStyle == DeleteStyle.Remove
            ? new HardDelete(this)
            : new SoftDelete(this);

        HardDeleteFragment = new HardDelete(this);
    }

    object IDocumentStorage.RawIdentityValue(object id)
    {
        return RawIdentityValue((TId)id);
    }

    public bool UseNumericRevisions { get;  }

    // TODO -- convert to a method in V8
    // this has to be a new instance every time because of how it gets the FromObject
    // renamed in Include() batches
    public ISelectClause SelectClauseWithDuplicatedFields
    {
        get
        {
            if (DuplicatedFields.Any())
            {
                var duplicatedFields = DuplicatedFields.Select(x => "d." + x.ColumnName).Where(x => !_selectFields.Contains(x));
                var allFields = _selectFields.Concat(duplicatedFields).ToArray();
                return new DuplicatedFieldSelectClause(TableName.QualifiedName, $"select {allFields.Join(", ")} from {_document.TableName.QualifiedName} as d",
                    allFields, typeof(T), this);
            }
            else
            {
                return this;
            }
        }
    }

    MetadataColumn[] IHaveMetadataColumns.MetadataColumns()
    {
        return _mapping.Schema.Table.Columns.OfType<MetadataColumn>().ToArray();
    }

    public IQueryableMemberCollection QueryMembers => _mapping.QueryMembers;

    public void TruncateDocumentStorage(IMartenDatabase database)
    {
        try
        {
            var sql = "truncate {0} cascade".ToFormat(TableName.QualifiedName);
            database.RunSql(sql);
        }
        catch (PostgresException e)
        {
            if (e.SqlState != PostgresErrorCodes.UndefinedTable)
            {
                throw;
            }
        }
    }

    public async Task TruncateDocumentStorageAsync(IMartenDatabase database, CancellationToken ct = default)
    {
        var sql = "truncate {0} cascade".ToFormat(TableName.QualifiedName);
        try
        {
            await database.RunSqlAsync(sql, ct).ConfigureAwait(false);
        }
        catch (PostgresException e)
        {
            if (e.SqlState != PostgresErrorCodes.UndefinedTable)
            {
                throw;
            }
        }
    }

    public void SetIdentity(T document, TId identity)
    {
        _setter(document, identity);
    }

    public void SetIdentityFromString(T document, string identityString)
    {
        _setFromString(document, identityString);
    }

    public void SetIdentityFromGuid(T document, Guid identityGuid)
    {
        _setFromGuid(document, identityGuid);
    }


    public TenancyStyle TenancyStyle => _mapping.TenancyStyle;

    public Type DocumentType => _mapping.DocumentType;

    public IReadOnlyList<DuplicatedField> DuplicatedFields { get; }

    public ISqlFragment ByIdFilter(TId id)
    {
        return new ByIdFilter(RawIdentityValue(id), _idType);
    }

    public IDeletion HardDeleteForId(TId id, string tenant)
    {
        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            return new Deletion(this, HardDeleteFragment, CompoundWhereFragment.And(new SpecificTenantFilter(tenant), ByIdFilter(id)))
            {
                Id = id
            };
        }

        return new Deletion(this, HardDeleteFragment, ByIdFilter(id)) { Id = id };
    }

    public void EjectById(IMartenSession session, object id)
    {
        var typedId = (TId)id;

        if (session.ItemMap.TryGetValue(typeof(T), out var dict))
        {
            if (dict is Dictionary<TId, T> d)
            {
                d.Remove(typedId);
            }
        }
    }

    public void RemoveDirtyTracker(IMartenSession session, object id)
    {
        session.ChangeTrackers.RemoveAll(x =>
        {
            if (x.Document is T doc)
            {
                return Identity(doc).Equals(id);
            }

            return false;
        });
    }

    public IDeletion HardDeleteForDocument(T document, string tenantId)
    {
        var id = Identity(document);

        var deletion = HardDeleteForId(id, tenantId);
        deletion.Document = document;

        return deletion;
    }

    public bool UseOptimisticConcurrency { get; }

    object IDocumentStorage<T>.IdentityFor(T document)
    {
        return Identity(document);
    }

    public Type SelectedType => typeof(T);

    public Type SourceType => typeof(T);


    public abstract TId AssignIdentity(T document, string tenantId, IMartenDatabase database);

    public DbObjectName TableName { get; }

    string ISelectClause.FromObject => TableName.QualifiedName;

    public Type IdType => typeof(TId);

    public Guid? VersionFor(T document, IMartenSession session)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return session.Versions.VersionFor<T, TId>(Identity(document));
    }

    public abstract void Store(IMartenSession session, T document);
    public abstract void Store(IMartenSession session, T document, Guid? version);
    public abstract void Store(IMartenSession session, T document, int revision);
    public abstract void Eject(IMartenSession session, T document);
    public abstract IStorageOperation Update(T document, IMartenSession session, string tenant);
    public abstract IStorageOperation Insert(T document, IMartenSession session, string tenant);
    public abstract IStorageOperation Upsert(T document, IMartenSession session, string tenant);

    public abstract IStorageOperation Overwrite(T document, IMartenSession session, string tenant);

    public IDeletion DeleteForDocument(T document, string tenant)
    {
        var id = Identity(document);

        var deletion = DeleteForId(id, tenant);
        deletion.Document = document;

        return deletion;
    }

    public IDeletion DeleteForId(TId id, string tenant)
    {
        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            return new Deletion(this, DeleteFragment, CompoundWhereFragment.And(new SpecificTenantFilter(tenant), ByIdFilter(id)))
            {
                Id = id
            };
        }

        return new Deletion(this, DeleteFragment, ByIdFilter(id))
        {
            Id = id
        };
    }

    public IOperationFragment DeleteFragment { get; }

    public IOperationFragment HardDeleteFragment { get; }

    public ISqlFragment FilterDocuments(ISqlFragment query, IMartenSession session)
    {
        var extras = extraFilters(query, session).ToList();

        if (extras.Count > 0)
        {
            extras.Add(query);
            return CompoundWhereFragment.And(extras);
        }

        return query;
    }

    public ISqlFragment? DefaultWhereFragment()
    {
        return _defaultWhere;
    }

    public abstract T? Load(TId id, IMartenSession session);
    public abstract Task<T?> LoadAsync(TId id, IMartenSession session, CancellationToken token);

    public abstract IReadOnlyList<T> LoadMany(TId[] ids, IMartenSession session);
    public abstract Task<IReadOnlyList<T>> LoadManyAsync(TId[] ids, IMartenSession session, CancellationToken token);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract TId Identity(T document);


    public void Apply(ICommandBuilder sql)
    {
        sql.Append(_selectClause);
    }

    public string[] SelectFields()
    {
        return _selectFields;
    }

    public abstract ISelector BuildSelector(IMartenSession session);

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement)
    {
        var selector = (ISelector<T>)BuildSelector(session);

        return LinqQueryParser.BuildHandler<T, TResult>(selector, statement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<T>(this, statistics);
    }

    public virtual object RawIdentityValue(TId id) => id;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NpgsqlParameter ParameterForId(TId id) => new() {Value = RawIdentityValue(id)};

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NpgsqlCommand BuildLoadCommand(TId id, string tenant)
    {
        return _mapping.TenancyStyle == TenancyStyle.Conjoined
            ? new NpgsqlCommand(_loaderSql) {
                Parameters = {
                    ParameterForId(id),
                    new() { Value = tenant }
                }
            }
            : new NpgsqlCommand(_loaderSql)
            {
                Parameters =
                {
                    ParameterForId(id)
                }
            };
    }

    public virtual NpgsqlParameter BuildManyIdParameter(TId[] ids) => new() { Value = ids };

    public NpgsqlCommand BuildLoadManyCommand(TId[] ids, string tenant)
    {
        return _mapping.TenancyStyle == TenancyStyle.Conjoined
            ? new NpgsqlCommand(_loadArraySql) {
                Parameters = {
                    BuildManyIdParameter(ids),
                    new() { Value = tenant }
                }
            }
            : new NpgsqlCommand(_loadArraySql)
            {
                Parameters =
                {
                    BuildManyIdParameter(ids)
                }
            };
    }

    private void determineDefaultWhereFragment()
    {
        var defaults = defaultFilters().ToArray();
        _defaultWhere = defaults.Length switch
        {
            0 => null,
            1 => defaults[0],
            _ => CompoundWhereFragment.And(defaults)
        };
    }

    private IEnumerable<ISqlFragment> extraFilters(ISqlFragment query, IMartenSession session)
    {
        if (_mapping.DeleteStyle == DeleteStyle.SoftDelete && !query.ContainsAny<ISoftDeletedFilter>())
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }

        if (TenancyStyle == TenancyStyle.Conjoined && !query.SpecifiesTenant())
        {
            yield return new DefaultTenantFilter(session.TenantId);
        }
    }

    private IEnumerable<ISqlFragment> defaultFilters()
    {
        if (_mapping.DeleteStyle == DeleteStyle.SoftDelete)
        {
            yield return ExcludeSoftDeletedFilter.Instance;
        }

        if (TenancyStyle == TenancyStyle.Conjoined)
        {
            yield return CurrentTenantFilter.Instance;
        }
    }


    protected T? load(TId id, IMartenSession session)
    {
        var command = BuildLoadCommand(id, session.TenantId);
        var selector = (ISelector<T>)BuildSelector(session);

        // TODO -- eliminate the downcast here!
        return session.As<QuerySession>().LoadOne(command, selector);
    }

    protected Task<T?> loadAsync(TId id, IMartenSession session, CancellationToken token)
    {
        var command = BuildLoadCommand(id, session.TenantId);
        var selector = (ISelector<T>)BuildSelector(session);

        // TODO -- eliminate the downcast here!
        return session.As<QuerySession>().LoadOneAsync(command, selector, token);
    }
}


internal class DuplicatedFieldSelectClause: ISelectClause, IModifyableFromObject
{
    private readonly string[] _selectFields;
    private readonly IDocumentStorage _parent;

    public DuplicatedFieldSelectClause(string fromObject, string selector, string[] selectFields, Type selectedType,
        IDocumentStorage parent)
    {
        FromObject = fromObject;
        _selectFields = selectFields;
        _parent = parent;
        SelectedType = selectedType;
    }

    public void Apply(ICommandBuilder builder)
    {
        builder.Append("select ");
        builder.Append(_selectFields.Join(", "));
        builder.Append(" from ");
        builder.Append(FromObject);
        builder.Append(" as d");
    }

    public string FromObject
    {
        get;
        set;
    }


    public Type SelectedType { get; }
    public string[] SelectFields()
    {
        return _selectFields;
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return _parent.BuildSelector(session);
    }

    public IQueryHandler<T> BuildHandler<T>(IMartenSession session, ISqlFragment topStatement, ISqlFragment currentStatement)
    {
        return _parent.BuildHandler<T>(session, topStatement, currentStatement);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return typeof(StatsSelectClause<>).CloseAndBuildAs<ISelectClause>(this, statistics, SelectedType);
    }
}
