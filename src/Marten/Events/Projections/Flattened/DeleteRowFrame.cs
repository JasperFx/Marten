#nullable enable

using System;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.CodeGeneration;
using Marten.Util;
using Weasel.Postgresql.Tables;

namespace Marten.Events.Projections.Flattened;

internal class DeleteRowFrame: MethodCall, IEventHandlingFrame
{
    private readonly MemberInfo[] _members;
    private readonly Table _table;
    private Variable? _event;

    private static readonly MethodInfo QueueSqlMethod =
        typeof(IDocumentOperations).GetMethod(nameof(IDocumentOperations.QueueSqlCommand),
            [typeof(string), typeof(object[])])!;

    public DeleteRowFrame(Table table, Type eventType, MemberInfo[] members): base(typeof(IDocumentOperations),
        QueueSqlMethod)
    {
        if (!members.Any())
        {
            throw new ArgumentOutOfRangeException(nameof(members), "Empty member list");
        }

        EventType = eventType;
        _table = table;
        _members = members;
    }

    public void Configure(EventProcessingFrame parent)
    {
        _event = parent.SpecificEvent;

        var sql = $"delete from {_table.Identifier} where {_table.PrimaryKeyColumns.Single()} = ?";
        Arguments[0] = Constant.ForString(sql);

        // TODO -- need a formal derived variable with this usage
        Arguments[1] = new Variable(_members.Last().GetMemberType(),
            $"{_event.Usage}.{_members.Select(x => x.Name).Join(".")}");
    }

    public Type EventType { get; }
}
