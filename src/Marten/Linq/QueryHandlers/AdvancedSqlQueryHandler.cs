#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Weasel.Postgresql;

namespace Marten.Linq.QueryHandlers;

internal class AdvancedSqlQueryHandler<T>: AdvancedSqlQueryHandlerBase<T>, IQueryHandler<IReadOnlyList<T>>
{
    public AdvancedSqlQueryHandler(IMartenSession session, char placeholder, string sql, object[] parameters): base(placeholder, sql, parameters)
    {
        RegisterResultType<T>(session);
    }

    public IReadOnlyList<T> Handle(DbDataReader reader, IMartenSession session)
    {
        var list = new List<T>();
        while (reader.Read())
        {
            var item = ((ISelector<T>)Selectors[0]).Resolve(reader);
            list.Add(item);
        }
        return list;
    }

    public override async IAsyncEnumerable<T> EnumerateResults(DbDataReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            yield return await ((ISelector<T>)Selectors[0]).ResolveAsync(reader, token).ConfigureAwait(false);
        }
    }
}

internal class AdvancedSqlQueryHandler<T1, T2>: AdvancedSqlQueryHandlerBase<(T1, T2)>, IQueryHandler<IReadOnlyList<(T1, T2)>>
{
    public AdvancedSqlQueryHandler(IMartenSession session, char placeholder, string sql, object[] parameters) : base(placeholder, sql, parameters)
    {
        RegisterResultType<T1>(session);
        RegisterResultType<T2>(session);
    }

    public IReadOnlyList<(T1, T2)> Handle(DbDataReader reader, IMartenSession session)
    {
        var list = new List<(T1, T2)>();
        while (reader.Read())
        {
            var item1 = ReadNestedRow<T1>(reader, 0);
            var item2 = ReadNestedRow<T2>(reader, 1);
            list.Add((item1, item2));
        }
        return list;
    }

    public override async IAsyncEnumerable<(T1, T2)> EnumerateResults(DbDataReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var item1 = await ReadNestedRowAsync<T1>(reader, 0, token).ConfigureAwait(false);
            var item2 = await ReadNestedRowAsync<T2>(reader, 1, token).ConfigureAwait(false);
            yield return (item1, item2);
        }
    }
}
internal class AdvancedSqlQueryHandler<T1, T2, T3>: AdvancedSqlQueryHandlerBase<(T1, T2, T3)>, IQueryHandler<IReadOnlyList<(T1, T2, T3)>>
{
    public AdvancedSqlQueryHandler(IMartenSession session, char placeholder, string sql, object[] parameters) : base(placeholder, sql, parameters)
    {
        RegisterResultType<T1>(session);
        RegisterResultType<T2>(session);
        RegisterResultType<T3>(session);
    }

    public IReadOnlyList<(T1, T2, T3)> Handle(DbDataReader reader, IMartenSession session)
    {
        var list = new List<(T1, T2, T3)>();
        while (reader.Read())
        {
            var item1 = ReadNestedRow<T1>(reader, 0);
            var item2 = ReadNestedRow<T2>(reader, 1);
            var item3 = ReadNestedRow<T3>(reader, 2);
            list.Add((item1, item2, item3));
        }
        return list;
    }

    public override async IAsyncEnumerable<(T1, T2, T3)> EnumerateResults(DbDataReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var item1 = await ReadNestedRowAsync<T1>(reader, 0, token).ConfigureAwait(false);
            var item2 = await ReadNestedRowAsync<T2>(reader, 1, token).ConfigureAwait(false);
            var item3 = await ReadNestedRowAsync<T3>(reader, 2, token).ConfigureAwait(false);
            yield return (item1, item2, item3);
        }
    }
}

internal abstract class AdvancedSqlQueryHandlerBase<TResult>
{
    protected readonly char Placeholder;
    protected readonly object[] Parameters;
    protected readonly string Sql;
    protected List<ISelector> Selectors = new();

    protected AdvancedSqlQueryHandlerBase(char placeholder, string sql, object[] parameters)
    {
        Sql = sql.TrimStart();
        Placeholder = placeholder;
        Parameters = parameters;
    }

    public List<Type> DocumentTypes { get; } = new();

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var firstParameter = Parameters.FirstOrDefault();

        if (Parameters.Length == 1 && firstParameter != null && firstParameter.IsAnonymousType())
        {
            builder.Append(Sql);
            builder.AddParameters(firstParameter);
        }
        else
        {
            var cmdParameters = builder.AppendWithParameters(Sql, Placeholder);
            if (cmdParameters.Length != Parameters.Length)
            {
                throw new InvalidOperationException("Wrong number of supplied parameters");
            }

            for (var i = 0; i < cmdParameters.Length; i++)
            {
                if (Parameters[i] == null)
                {
                    cmdParameters[i].Value = DBNull.Value;
                }
                else
                {
                    cmdParameters[i].Value = Parameters[i];
                    cmdParameters[i].NpgsqlDbType =
                        PostgresqlProvider.Instance.ToParameterType(Parameters[i].GetType());
                }
            }
        }
    }

    protected async Task<T> ReadNestedRowAsync<T>(DbDataReader reader, int rowIndex, CancellationToken token)
    {
        var innerReader = reader.GetData(rowIndex) ??
                          throw new ArgumentException("Invalid row index", nameof(rowIndex));

        await using (innerReader.ConfigureAwait(false))
        {
            if (await innerReader.ReadAsync(token).ConfigureAwait(false))
            {
                return await ((ISelector<T>)Selectors[rowIndex]).ResolveAsync(innerReader, token).ConfigureAwait(false);
            }
        }
        return default;
    }

    protected T ReadNestedRow<T>(DbDataReader reader, int rowIndex)
    {
        var innerReader = reader.GetData(rowIndex) ??
                          throw new ArgumentException("Invalid row index", nameof(rowIndex));

        using (innerReader)
        {
            if (innerReader.Read())
            {
                return ((ISelector<T>)Selectors[rowIndex]).Resolve(innerReader);
            }
        }
        return default;
    }

    protected ISelectClause GetSelectClause<T>(IMartenSession session) where T : notnull
    {
        if (typeof(T) == typeof(string))
        {
            return new ScalarStringSelectClause(string.Empty, string.Empty);
        }

        if (PostgresqlProvider.Instance.HasTypeMapping(typeof(T)))
        {
            return typeof(ScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(string.Empty, string.Empty, typeof(T));
        }

        if (typeof(T).GetProperty("Id") == null && typeof(T).GetProperty("id") == null)
        {
            return new DataSelectClause<T>(string.Empty, string.Empty);
        }
        return session.StorageFor<T>();
    }

    protected void RegisterResultType<T>(IMartenSession session)
    {
        var selectClause = GetSelectClause<T>(session);
        Selectors.Add(selectClause.BuildSelector(session));
        if (selectClause is IDocumentStorage)
        {
            DocumentTypes.Add(typeof(T));
        }
    }

    public Task<int> StreamJson(Stream stream, DbDataReader reader, CancellationToken token)
    {
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyList<TResult>> HandleAsync(DbDataReader reader, IMartenSession session,
        CancellationToken token)
    {
        var list = new List<TResult>();
        await foreach (var result in EnumerateResults(reader, token).ConfigureAwait(false))
        {
            list.Add(result);
        }

        return list;
    }

    public abstract IAsyncEnumerable<TResult> EnumerateResults(DbDataReader reader, CancellationToken token);
}
