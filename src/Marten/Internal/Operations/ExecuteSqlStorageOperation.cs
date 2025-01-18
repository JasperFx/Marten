using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Services;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Internal.Operations;

internal class ExecuteSqlStorageOperation: IStorageOperation, NoDataReturnedCall
{
    private readonly string _commandText;
    private readonly object[] _parameterValues;

    public ExecuteSqlStorageOperation(string commandText, params object[] parameterValues)
    {
        _commandText = commandText.TrimEnd(';');
        _parameterValues = parameterValues;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        if (_parameterValues is [{ } first] && (first.IsAnonymousType() || first is IDictionary { Keys: ICollection<string> }))
        {
            builder.Append(_commandText);
            builder.AddParameters(first);
            return;
        }

        var parameters = builder.AppendWithParameters(_commandText);
        if (parameters.Length != _parameterValues.Length)
        {
            throw new InvalidOperationException(
                $"Wrong number of parameter values to SQL '{_commandText}', got {_parameterValues.Length} parameters");
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            if (_parameterValues[i] == null)
            {
                parameters[i].Value = DBNull.Value;
            }
            else
            {
                var dbType = PostgresqlProvider.Instance.TryGetDbType(_parameterValues[i].GetType());
                if (dbType != null)
                {
                    parameters[i].NpgsqlDbType = dbType.Value;
                }

                parameters[i].Value = _parameterValues[i];
            }
        }
    }

    public Type DocumentType => typeof(StorageFeatures);

    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // nothing
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
