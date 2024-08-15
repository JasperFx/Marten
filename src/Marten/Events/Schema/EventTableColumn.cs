using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Marten.Internal.CodeGeneration;
using Marten.Linq.Parsing;
using JasperFx.Core.Reflection;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Schema;

internal class EventTableColumn: TableColumn, IEventTableColumn
{
    private readonly Expression<Func<IEvent, object>> _eventMemberExpression;

    public
        EventTableColumn(string name, Expression<Func<IEvent, object>> eventMemberExpression): base(name, "varchar")
    {
        _eventMemberExpression = eventMemberExpression;
        Member = MemberFinder.Determine(eventMemberExpression).Single();
        var memberType = Member.GetMemberType();
        Type = PostgresqlProvider.Instance.GetDatabaseType(memberType, EnumStorage.AsInteger);
        NpgsqlDbType = PostgresqlProvider.Instance.ToParameterType(memberType);
    }

    public MemberInfo Member { get; }

    public NpgsqlDbType NpgsqlDbType { get; set; }

    public void GenerateSelectorCodeSync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNull(index, () =>
        {
            method.AssignMemberFromReader(null, index, _eventMemberExpression);
        });
    }

    public void GenerateSelectorCodeAsync(GeneratedMethod method, EventGraph graph, int index)
    {
        method.IfDbReaderValueIsNotNullAsync(index, () =>
        {
            method.AssignMemberFromReaderAsync(null, index, _eventMemberExpression);
        });
    }

    public virtual void GenerateAppendCode(GeneratedMethod method, EventGraph graph, int index, AppendMode full)
    {
        method.Frames.Code(
            $"var parameter{index} = parameterBuilder.{nameof(IGroupedParameterBuilder.AppendParameter)}({{0}}.{Member.Name});", Use.Type<IEvent>());

        method.Frames.Code($"parameter{index}.{nameof(NpgsqlParameter.NpgsqlDbType)} = {{0}};", NpgsqlDbType);

    }

    public virtual string ValueSql(EventGraph graph, AppendMode mode)
    {
        return "?";
    }
}
