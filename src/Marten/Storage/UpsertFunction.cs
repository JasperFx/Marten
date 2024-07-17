using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Schema.Arguments;
using Marten.Storage.Metadata;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;

namespace Marten.Storage;

internal class UpsertFunction: Function
{
    protected readonly string _andTenantWhereClause;
    private readonly bool _disableConcurrency;
    protected readonly DocumentMapping _mapping;
    protected readonly DbObjectName _tableName;
    protected readonly string _tenantWhereClause;

    public readonly IList<UpsertArgument> Arguments = new List<UpsertArgument>();
    protected readonly string _primaryKeyFields;
    protected readonly string _versionSourceTable;
    protected readonly string _versionColumnName;
    protected readonly string _tenantVersionWhereClause;
    protected readonly string _andTenantVersionWhereClause;

    public UpsertFunction(DocumentMapping mapping, DbObjectName identifier = null, bool disableConcurrency = false):
        base(identifier ?? mapping.UpsertFunction)
    {
        _mapping = mapping;
        _disableConcurrency = disableConcurrency;
        if (mapping == null)
        {
            throw new ArgumentNullException(nameof(mapping));
        }

        _tableName = mapping.TableName;

        var table = new DocumentTable(mapping);

        var idType = mapping.IdType;
        var pgIdType = PostgresqlProvider.Instance.GetDatabaseType(idType, mapping.EnumStorage);

        Arguments.Add(new UpsertArgument
        {
            Arg = "docId", PostgresType = pgIdType, Column = "id", Members = new[] { mapping.IdMember }, ParameterValue = mapping.CodeGen.ParameterValue
        });

        Arguments.Add(new DocJsonBodyArgument());

        Arguments.AddRange(mapping.DuplicatedFields.Where(x => !x.OnlyForSearching).Select(x => x.UpsertArgument));

        // These two arguments need to be added this way
        if (mapping.Metadata.Version.Enabled)
        {
            Arguments.Add(new VersionArgument());
        }

        if (mapping.Metadata.DotNetType.Enabled)
        {
            Arguments.Add(new DotNetTypeArgument());
        }

        AddIfActive(mapping.Metadata.CorrelationId);
        AddIfActive(mapping.Metadata.CausationId);
        AddIfActive(mapping.Metadata.LastModifiedBy);
        AddIfActive(mapping.Metadata.Headers);


        if (mapping.IsHierarchy())
        {
            Arguments.Add(new DocTypeArgument());
        }

        if (mapping.UseOptimisticConcurrency)
        {
            Arguments.Add(new CurrentVersionArgument());
        }
        else if (mapping.UseNumericRevisions)
        {
            Arguments.Add(new RevisionArgument());
        }

        if (_mapping.UseVersionFromMatchingStream)
        {
            _versionSourceTable = $"{_mapping.StoreOptions.Events.DatabaseSchemaName}.mt_streams";
            _versionColumnName = "version";
        }
        else
        {
            _versionSourceTable = _tableName.QualifiedName;
            _versionColumnName = "mt_version";
        }

        if (mapping.TenancyStyle == TenancyStyle.Conjoined)
        {
            Arguments.Add(new TenantIdArgument());
            _tenantWhereClause = $"{_tableName.QualifiedName}.{TenantIdColumn.Name} = {TenantIdArgument.ArgName}";
            _andTenantWhereClause = $" and {_tenantWhereClause}";


            if (_mapping.UseVersionFromMatchingStream)
            {
                _tenantVersionWhereClause = $"{_versionSourceTable}.{TenantIdColumn.Name} = {TenantIdArgument.ArgName}";
                _andTenantVersionWhereClause = $" and {_tenantVersionWhereClause}";
            }
        }

        _primaryKeyFields = table.Columns.Where(x => x.IsPrimaryKey).Select(x => x.Name).Join(", ");
    }

    public void AddIfActive(MetadataColumn column)
    {
        if (column.Enabled)
        {
            Arguments.Add(column.ToArgument());
        }
    }

    public override void WriteCreateStatement(Migrator rules, TextWriter writer)
    {
        var ordered = OrderedArguments();

        var argList = ordered.Select(x => x.ArgumentDeclaration()).Join(", ");


        var systemUpdates = _mapping.Metadata.LastModified.Enabled
            ? new[] { $"{SchemaConstants.LastModifiedColumn} = transaction_timestamp()" }
            : new string[0];
        var updates = ordered.Where(x => x.Column != "id" && x.Column.IsNotEmpty())
            .Select(x => $"\"{x.Column}\" = {x.Arg}").Concat(systemUpdates).Join(", ");

        var insertColumns = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => $"\"{x.Column}\"").ToList();
        var valueListColumns = ordered.Where(x => x.Column.IsNotEmpty()).Select(x => x.Arg).ToList();

        if (_mapping.Metadata.LastModified.Enabled)
        {
            insertColumns.Add(SchemaConstants.LastModifiedColumn);
            valueListColumns.Add("transaction_timestamp()");
        }
        if (_mapping.Metadata.CreatedAt.Enabled)
        {
            insertColumns.Add(SchemaConstants.CreatedAtColumn);
            valueListColumns.Add("transaction_timestamp()");
        }

        var inserts = insertColumns.Join(", ");
        var valueList = valueListColumns.Join(", ");

        var whereClauses = new List<string>();

        if (Arguments.Any(x => x is RevisionArgument) && !_disableConcurrency)
        {
            whereClauses.Add($"revision > {_tableName.QualifiedName}.{SchemaConstants.VersionColumn}");
        }

        if (Arguments.Any(x => x is CurrentVersionArgument) && !_disableConcurrency)
        {
            whereClauses.Add($"{_tableName.QualifiedName}.{SchemaConstants.VersionColumn} = current_version");
        }

        if (Arguments.Any(x => x is TenantIdArgument))
        {
            whereClauses.Add(_tenantWhereClause);
        }

        if (whereClauses.Any())
        {
            updates += " where " + whereClauses.Join(" and ");
        }

        var securityDeclaration = rules.UpsertRights == SecurityRights.Invoker
            ? "SECURITY INVOKER"
            : "SECURITY DEFINER";

        writeFunction(writer, argList, securityDeclaration, inserts, valueList, updates);
    }

    protected virtual void writeFunction(TextWriter writer, string argList, string securityDeclaration,
        string inserts,
        string valueList, string updates)
    {
        var revisionModification = _mapping.UseVersionFromMatchingStream
            ? "revision = current_version;"
            : "revision = current_version + 1;";

        if (_mapping.Metadata.Revision.Enabled)
        {
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS INTEGER LANGUAGE plpgsql {
    securityDeclaration
} AS $function$
DECLARE
  final_version INTEGER;
  current_version INTEGER;
BEGIN

if revision <= 1 then
  SELECT {_versionColumnName} FROM {_versionSourceTable} into current_version WHERE id = docId {_andTenantVersionWhereClause};
  if current_version is not null then
    {revisionModification}
  end if;
end if;

INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ({_primaryKeyFields})
  DO UPDATE SET {updates};

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId {_andTenantWhereClause};
  RETURN final_version;
END;
$function$;
");

        }
        else if (_mapping.Metadata.Version.Enabled)
        {
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
    securityDeclaration
} AS $function$
DECLARE
  final_version uuid;
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ({_primaryKeyFields})
  DO UPDATE SET {updates};

  SELECT mt_version FROM {_tableName.QualifiedName} into final_version WHERE id = docId {_andTenantWhereClause};
  RETURN final_version;
END;
$function$;
");
        }
        else
        {
            writer.WriteLine($@"
CREATE OR REPLACE FUNCTION {Identifier.QualifiedName}({argList}) RETURNS UUID LANGUAGE plpgsql {
    securityDeclaration
} AS $function$
DECLARE
  final_version uuid;
BEGIN
INSERT INTO {_tableName.QualifiedName} ({inserts}) VALUES ({valueList})
  ON CONFLICT ({_primaryKeyFields})
  DO UPDATE SET {updates};

  RETURN '{Guid.Empty}';
END;
$function$;
");
        }
    }

    public UpsertArgument[] OrderedArguments()
    {
        return Arguments.OrderBy(x => x.Arg).ToArray();
    }
}
