using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

internal class EventProgressWrite: IStorageOperation
{
    private readonly string _key;
    private readonly long _number;
    private readonly DbObjectName _sproc;

    public EventProgressWrite(EventGraph events, string key, long number)
    {
        _sproc = new PostgresqlObjectName(events.DatabaseSchemaName, "mt_mark_event_progression");
        _key = key;
        _number = number;
    }

    public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
    {
        var nameArg = builder.AddParameter(_key, NpgsqlDbType.Varchar);
        var numberArg = builder.AddParameter(_number, NpgsqlDbType.Bigint);
        builder.Append($"select {_sproc}(:{nameArg.ParameterName}, :{numberArg.ParameterName})");
    }

    public Type DocumentType => typeof(IEvent);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // Nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Other;
    }
}
