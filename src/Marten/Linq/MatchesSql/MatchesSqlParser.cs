#nullable enable
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.Core.Reflection;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.MatchesSql;

public class MatchesSqlParser: IMethodCallParser
{
    private static readonly MethodInfo _sqlMethod =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql),
            new[] { typeof(object), typeof(string), typeof(object[]) })!;

    private static readonly MethodInfo _sqlMethodWithPlaceholder =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql),
            new[] { typeof(object), typeof(char), typeof(string), typeof(object[]) })!;

    private static readonly MethodInfo _fragmentMethod =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesSql),
            new[] { typeof(object), typeof(ISqlFragment) })!;

    public bool Matches(MethodCallExpression expression)
    {
        return Equals(expression.Method, _sqlMethod) || Equals(expression.Method, _fragmentMethod);
    }

    public ISqlFragment? Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        if (expression.Method.Equals(_sqlMethod))
        {
            return new WhereFragment(expression.Arguments[1].Value().As<string>(),
                expression.Arguments[2].Value().As<object[]>());
        }

        if (expression.Method.Equals(_sqlMethodWithPlaceholder))
        {
            return new CustomizableWhereFragment(expression.Arguments[1].Value().As<string>(),
                expression.Arguments[2].Value().As<char>().ToString(),
                expression.Arguments[3].Value().As<object[]>());
        }

        if (expression.Method.Equals(_fragmentMethod))
        {
            return expression.Arguments[1].Value() as ISqlFragment;
        }

        return null;
    }
}

public class MatchesJsonPathParser: IMethodCallParser
{
    private static readonly MethodInfo _sqlMethod =
        typeof(MatchesSqlExtensions).GetMethod(nameof(MatchesSqlExtensions.MatchesJsonPath),
            new[] { typeof(object), typeof(string), typeof(object[]) })!;

    public bool Matches(MethodCallExpression expression)
    {
        return Equals(expression.Method, _sqlMethod);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var arguments = expression.Arguments[2].Value().As<object[]>().Select(x => new CommandParameter(x)).ToArray();

        return new LiteralSqlWithJsonPath(expression.Arguments[1].Value().As<string>(), arguments);
    }
}

internal class LiteralSqlWithJsonPath : ISqlFragment
{
    private readonly string _sql;
    private readonly object[] _parameters;

    public LiteralSqlWithJsonPath(string sql, object[] parameters)
    {
        _sql = sql;
        _parameters = parameters;
    }

    public void Apply(ICommandBuilder builder)
    {
        var parameters = builder.AppendWithParameters(_sql, '^');
        for (var i = 0; i < parameters.Length; i++)
        {
            parameters[i].Value = _parameters[i];
        }
    }
}
