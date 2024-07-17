using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Internal;
using Marten.Internal.Operations;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Events.Operations;

public abstract class QuickAppendEventsOperationBase : IStorageOperation
{
    public QuickAppendEventsOperationBase(StreamAction stream)
    {
        Stream = stream;
    }

    public StreamAction Stream { get; }

    public OperationRole Role()
    {
        return OperationRole.Events;
    }

    public Type DocumentType => typeof(IEvent);

    public override string ToString()
    {
        return $"Append {Stream.Events.Select(x => x.EventTypeName).Join(", ")} to event stream {Stream}";
    }

    public abstract void ConfigureCommand(ICommandBuilder builder, IMartenSession session);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        if (reader.Read())
        {
            var values = reader.GetFieldValue<long[]>(0);

            Stream.ApplyEndingVersion(values[0]);

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                Stream.Events[i - 1].Sequence = values[i];
            }
        }
    }

    protected void writeId(NpgsqlParameter[] parameters)
    {
        parameters[0].NpgsqlDbType = NpgsqlDbType.Uuid;
        parameters[0].Value = Stream.Id;
    }

    protected void writeKey(NpgsqlParameter[] parameters)
    {
        parameters[0].NpgsqlDbType = NpgsqlDbType.Varchar;
        parameters[0].Value = Stream.Key;
    }

    protected void writeBasicParameters(NpgsqlParameter[] parameters, IMartenSession session)
    {
        parameters[1].NpgsqlDbType = NpgsqlDbType.Varchar;
        parameters[1].Value = Stream.AggregateTypeName.IsEmpty() ? DBNull.Value : Stream.AggregateTypeName;
        parameters[2].NpgsqlDbType = NpgsqlDbType.Varchar;
        parameters[2].Value = Stream.TenantId;
        parameters[3].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Uuid;
        parameters[3].Value = Stream.Events.Select(x => x.Id).ToArray();
        parameters[4].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        parameters[4].Value = Stream.Events.Select(x => x.EventTypeName).ToArray();
        parameters[5].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        parameters[5].Value = Stream.Events.Select(x => x.DotNetTypeName).ToArray();
        parameters[6].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
        parameters[6].Value = Stream.Events.Select(e => session.Serializer.ToJson(e.Data)).ToArray();
    }

    protected void writeCausationIds(int index, NpgsqlParameter[] parameters)
    {
        parameters[index].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        parameters[index].Value = Stream.Events.Select(x => x.CausationId).ToArray();
    }

    protected void writeCorrelationIds(int index, NpgsqlParameter[] parameters)
    {
        parameters[index].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Varchar;
        parameters[index].Value = Stream.Events.Select(x => x.CorrelationId).ToArray();
    }

    protected void writeHeaders(int index, NpgsqlParameter[] parameters, IMartenSession session)
    {
        parameters[index].NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Jsonb;
        parameters[index].Value = Stream.Events.Select(x => session.Serializer.ToJson(x.Headers)).ToArray();
    }

    public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var values = await reader.GetFieldValueAsync<long[]>(0, token).ConfigureAwait(false);

            Stream.ApplyEndingVersion(values[0]);

            // Ignore the first value
            for (int i = 1; i < values.Length; i++)
            {
                // Only setting the sequence to aid in tombstone processing
                Stream.Events[i - 1].Sequence = values[i];
            }
        }
    }
}
