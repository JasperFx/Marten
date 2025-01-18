using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using Marten.Events.CodeGeneration;
using Weasel.Core;

namespace Marten.Events.Projections.Flattened;

internal class CallUpsertFunctionFrame: MethodCall, IEventHandlingFrame
{
    private readonly List<IColumnMap> _columnMaps;
    private readonly DbObjectName _functionIdentifier;
    private readonly MemberInfo[] _members;

    private static readonly MethodInfo QueueSqlMethod =
        typeof(IDocumentOperations).GetMethod(nameof(IDocumentOperations.QueueSqlCommand),
            [typeof(string), typeof(object[])])!;

    public CallUpsertFunctionFrame(Type eventType, DbObjectName functionIdentifier, List<IColumnMap> columnMaps,
        MemberInfo[] members): base(typeof(IDocumentOperations), QueueSqlMethod)
    {
        _functionIdentifier = functionIdentifier ?? throw new ArgumentNullException(nameof(functionIdentifier));
        _columnMaps = columnMaps;
        _members = members;
        EventType = eventType;
    }

    public void Configure(EventProcessingFrame parent)
    {
        var pk = $"{parent.SpecificEvent.Usage}.{_members.Select(x => x.Name).Join(".")}";
        var sql =
            _columnMaps.Any(x => x.RequiresInput)
                ? $"select {_functionIdentifier.QualifiedName}(?, {_columnMaps.Where(x => x.RequiresInput).Select(x => "?").Join(", ")});"
                : $"select {_functionIdentifier.QualifiedName}(?);";

        var values = _columnMaps.Where(x => x.RequiresInput).Select(x => x.ToValueAccessorCode(parent.SpecificEvent))
            .Join(", ");

        Arguments[0] = Constant.ForString(sql);
        Arguments[1] = _columnMaps.Any(x => x.RequiresInput)
            ? new Variable(typeof(object[]), $"{pk}, {values}")
            : new Variable(typeof(object[]), $"{pk}");
    }

    public Type EventType { get; }
}
