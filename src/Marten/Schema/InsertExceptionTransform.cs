using System;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Npgsql;

namespace Marten.Schema;

public sealed class InsertExceptionTransform<T>: IExceptionTransform
{
    private readonly object id;
    private readonly string tableName;

    public InsertExceptionTransform(object id, string tableName)
    {
        this.id = id;
        this.tableName = tableName;
    }

    public bool TryTransform(Exception original, out Exception transformed)
    {
        transformed = null;

        if (original is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgresException &&
            postgresException.TableName == tableName)
        {
            transformed = new DocumentAlreadyExistsException(original, typeof(T), id);
            return true;
        }

        return false;
    }
}
