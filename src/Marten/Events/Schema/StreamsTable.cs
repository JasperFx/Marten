using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.CodeGeneration;
using Marten.Events.Archiving;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Parsing;
using Marten.Storage;
using Marten.Storage.Metadata;
using JasperFx.Core.Reflection;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using FindMembers = Marten.Linq.Parsing.FindMembers;

namespace Marten.Events.Schema;

internal class StreamsTable: Table
{
    public const string TableName = "mt_streams";

    public StreamsTable(EventGraph events): base(new PostgresqlObjectName(events.DatabaseSchemaName, TableName))
    {
        var idColumn = events.StreamIdentity == StreamIdentity.AsGuid
            ? new StreamTableColumn("id", x => x.Id)
            : new StreamTableColumn("id", x => x.Key);

        AddColumn(idColumn).AsPrimaryKey();

        if (events.TenancyStyle == TenancyStyle.Conjoined)
        {
            AddColumn<TenantIdColumn>().AsPrimaryKey();
        }

        AddColumn(new StreamTableColumn("type", x => x.AggregateTypeName)).AllowNulls();

        AddColumn(new StreamTableColumn("version", x => x.Version)).AllowNulls();

        AddColumn(new StreamTableColumn("timestamp", x => x.Timestamp)
        {
            Type = "timestamptz", Writes = false, AllowNulls = false, DefaultExpression = "(now())"
        });


        AddColumn("snapshot", "jsonb");
        AddColumn("snapshot_version", "integer");

        AddColumn(new StreamTableColumn("created", x => x.Created)
        {
            Type = "timestamptz", Writes = false, AllowNulls = false, DefaultExpression = "(now())"
        });

        if (events.TenancyStyle != TenancyStyle.Conjoined)
        {
            AddColumn<TenantIdColumn>();
        }

        AddColumn<IsArchivedColumn>();
    }
}

internal interface IStreamTableColumn
{
    bool Reads { get; }
    bool Writes { get; }

    string Name { get; }
    void GenerateAppendCode(GeneratedMethod method, int index);

    public void GenerateSelectorCodeAsync(GeneratedMethod method, int index);
    public void GenerateSelectorCodeSync(GeneratedMethod method, int index);
}

internal class StreamTableColumn: TableColumn, IStreamTableColumn
{
    private readonly MemberInfo _member;
    private readonly Expression<Func<StreamAction, object>> _memberExpression;

    public StreamTableColumn(string name, Expression<Func<StreamAction, object>> memberExpression): base(name,
        "varchar")
    {
        _memberExpression = memberExpression;
        _member = FindMembers.Determine(memberExpression).Single();
        var memberType = _member.GetMemberType();
        Type = PostgresqlProvider.Instance.GetDatabaseType(memberType, EnumStorage.AsInteger);
        NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(memberType);
    }

    public NpgsqlDbType NpgsqlDbType { get; set; }

    public bool Reads { get; set; } = true;
    public bool Writes { get; set; } = true;

    public void GenerateAppendCode(GeneratedMethod method, int index)
    {
        method.SetParameterFromMember(index, _memberExpression);
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, int index)
    {
        method.AssignMemberFromReader(null, index, typeof(StreamAction), _member.Name);
    }

    public void GenerateSelectorCodeSync(GeneratedMethod method, int index)
    {
        method.AssignMemberFromReaderAsync(null, index, typeof(StreamAction), _member.Name);
    }
}
