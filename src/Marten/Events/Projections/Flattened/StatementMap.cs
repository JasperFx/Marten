using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.Core.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Internal.CodeGeneration;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

public class StatementMap<T>: IEventHandler
{
    private readonly List<IColumnMap> _columnMaps = new();
    private readonly FlatTableProjection _parent;
    private readonly MemberInfo[] _pkMembers;
    private DbObjectName _functionIdentifier;

    public StatementMap(FlatTableProjection parent, MemberInfo[] pkMembers)
    {
        _parent = parent;
        _pkMembers = pkMembers;
    }

    Type IEventHandler.EventType => typeof(T);

    IEventHandlingFrame IEventHandler.BuildFrame(EventGraph events, Table table)
    {
        return new CallUpsertFunctionFrame(typeof(T), _functionIdentifier, _columnMaps,
            determinePkMembers(events).ToArray());
    }

    bool IEventHandler.AssertValid(EventGraph events, out string? message)
    {
        message = null;
        return true;
    }

    IEnumerable<ISchemaObject> IEventHandler.BuildObjects(EventGraph events, Table table)
    {
        var functionName = $"mt_upsert_{table.Identifier.Name}_{typeof(T).NameInCode().Sanitize()}";
        _functionIdentifier = new PostgresqlObjectName(table.Identifier.Schema, functionName);

        yield return new FlatTableUpsertFunction(_functionIdentifier, table, _columnMaps);
    }

    private IEnumerable<MemberInfo> determinePkMembers(EventGraph events)
    {
        if (_pkMembers.Any())
        {
            yield return ReflectionHelper.GetProperty<IEvent<T>>(x => x.Data);
            foreach (var member in _pkMembers) yield return member;

            yield break;
        }

        if (events.StreamIdentity == StreamIdentity.AsGuid)
        {
            yield return ReflectionHelper.GetProperty<IEvent<T>>(x => x.StreamId);
        }
        else
        {
            yield return ReflectionHelper.GetProperty<IEvent<T>>(x => x.StreamKey);
        }
    }

    /// <summary>
    ///     Map a single value in the event data to a column in the table
    /// </summary>
    /// <param name="members"></param>
    /// <param name="columnName">Explicitly define the column name, otherwise this will be derived from the members</param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public Table.ColumnExpression Map<TValue>(Expression<Func<T, TValue>> members, string? columnName = null)
    {
        var map = new MemberMap<T, TValue>(members, columnName, ColumnMapType.Value);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }


    /// <summary>
    ///     Directs the projection to increment the designated column by the value of the event data values
    /// </summary>
    /// <param name="members"></param>
    /// <param name="columnName"></param>
    /// <typeparam name="TValue"></typeparam>
    /// <returns></returns>
    public Table.ColumnExpression Increment<TValue>(Expression<Func<T, TValue>> members, string? columnName = null)
    {
        var map = new MemberMap<T, TValue>(members, columnName, ColumnMapType.Increment);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Directs the projection to increment by one the value of the designated column
    /// </summary>
    /// <param name="columnName"></param>
    /// <returns></returns>
    public Table.ColumnExpression Increment(string columnName)
    {
        var map = new IncrementMap(columnName);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Directs the projection to decrement the designated column by the value of the event data values
    /// </summary>
    /// <param name="columnName"></param>
    /// <returns></returns>
    public Table.ColumnExpression Decrement<TValue>(Expression<Func<T, TValue>> members, string? columnName = null)
    {
        var map = new MemberMap<T, TValue>(members, columnName, ColumnMapType.Decrement);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Directs the projection to decrement by one the value of the designated column
    /// </summary>
    /// <param name="columnName"></param>
    /// <returns></returns>
    public Table.ColumnExpression Decrement(string columnName)
    {
        var map = new DecrementMap(columnName);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Set the designated column value to the explicit string value when this event type is encountered
    /// </summary>
    /// <param name="columnName"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public Table.ColumnExpression SetValue(string columnName, string value)
    {
        var map = new SetStringValueMap(columnName, value);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }

    /// <summary>
    ///     Set the designated column value to the explicit integer value when this event type is encountered
    /// </summary>
    /// <param name="columnName"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public Table.ColumnExpression SetValue(string columnName, int value)
    {
        var map = new SetIntValueMap(columnName, value);
        _columnMaps.Add(map);

        return map.ResolveColumn(_parent.Table);
    }
}
