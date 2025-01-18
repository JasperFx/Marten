#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Services;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Linq.QueryHandlers;

internal class UserSuppliedQueryHandler<T>: IQueryHandler<IReadOnlyList<T>>
{
    private readonly object[] _parameters;
    private readonly ISelectClause _selectClause;
    private readonly ISelector<T> _selector;
    private readonly string _sql;

    public UserSuppliedQueryHandler(IMartenSession session, string sql, object[] parameters)
    {
        _sql = sql.TrimStart();
        _parameters = parameters;
        SqlContainsCustomSelect = _sql.StartsWith("select", StringComparison.OrdinalIgnoreCase)
                                  || IsWithFollowedBySelect(_sql);

        _selectClause = GetSelectClause(session);
        _selector = (ISelector<T>)_selectClause.BuildSelector(session);
    }

    public bool SqlContainsCustomSelect { get; }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        if (!SqlContainsCustomSelect)
        {
            _selectClause.Apply(builder);

            if (_sql.StartsWith("where", StringComparison.OrdinalIgnoreCase) ||
                _sql.StartsWith("order", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" ");
            }
            else if (!_sql.Contains(" where ", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" where ");
            }
        }

        if (_parameters is [{ } first] && (first.IsAnonymousType() || first is IDictionary { Keys: ICollection<string> }))
        {
            builder.Append(_sql);
            builder.AddParameters(first);
            return;
        }

        var cmdParameters = builder.AppendWithParameters(_sql);
        if (cmdParameters.Length != _parameters.Length)
        {
            throw new InvalidOperationException("Wrong number of supplied parameters");
        }

        for (var i = 0; i < cmdParameters.Length; i++)
        {
            if (_parameters[i] == null!)
            {
                cmdParameters[i].Value = DBNull.Value;
            }
            else
            {
                cmdParameters[i].Value = _parameters[i];
                cmdParameters[i].NpgsqlDbType =
                    PostgresqlProvider.Instance.ToParameterType(_parameters[i].GetType());
            }
        }
    }

    public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session)
    {
        var list = new List<T>();

        while (reader.Read())
        {
            var item = _selector.Resolve(reader);
            list.Add(item);
        }

        return list;
    }

    public async Task<IReadOnlyList<T>> HandleAsync(DbDataReader reader, IMartenSession session,
        CancellationToken token)
    {
        var list = new List<T>();

        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var item = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);
            list.Add(item);
        }

        return list;
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        return reader.As<NpgsqlDataReader>().StreamMany(stream, token);
    }


    private ISelectClause GetSelectClause(IMartenSession session)
    {
        if (typeof(T) == typeof(string))
        {
            return new ScalarStringSelectClause(string.Empty, string.Empty);
        }

        if (PostgresqlProvider.Instance.HasTypeMapping(typeof(T)))
        {
            return typeof(ScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(string.Empty, string.Empty, typeof(T));
        }


        if (SqlContainsCustomSelect)
        {
            return new DataSelectClause<T>(string.Empty, string.Empty);
        }

        return session.StorageFor(typeof(T));
    }

    private static bool IsWithFollowedBySelect(string sql)
    {
        var parenthesesLevel = 0;
        var isWithBlockDetected = false;

         for (var i = 0; i < sql.Length; i++)
         {
             var c = sql[i];

            // Check for parentheses to handle nested structures
            if (c == '(')
            {
                parenthesesLevel++;
            }
            else if (c == ')')
            {
                parenthesesLevel--;
            }

            // Detect the beginning of the WITH block
            if (!isWithBlockDetected && i < sql.Length - 4 && sql.Substring(i, 4).Equals("with", StringComparison.OrdinalIgnoreCase))
            {
                isWithBlockDetected = true;
            }

            // Detect the beginning of the SELECT block only if WITH block is detected and at top-level
            if (isWithBlockDetected && i < sql.Length - 6 && sql.Substring(i, 6).Equals("select", StringComparison.OrdinalIgnoreCase) && parenthesesLevel == 0)
            {
                return true;
            }
        }

        return false;
    }
}
