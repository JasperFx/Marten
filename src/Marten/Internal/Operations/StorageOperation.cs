using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Internal.DirtyTracking;
using Marten.Schema;
using Marten.Schema.Identity;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;

namespace Marten.Internal.Operations;

public interface IRevisionedOperation
{
    int Revision { get; set; }
    bool IgnoreConcurrencyViolation { get; set; }
}

public abstract class StorageOperation<T, TId>: IDocumentStorageOperation, IExceptionTransform, IRevisionedOperation
{
    private readonly T _document;
    protected readonly TId _id;
    private readonly string _tableName;
    private readonly Dictionary<TId, Guid> _versions;
    protected Guid _version = CombGuidIdGeneration.NewGuid();

    public StorageOperation(T document, TId id, Dictionary<TId, Guid> versions, DocumentMapping mapping)
    {
        _document = document;
        _id = id;
        _versions = versions;
        _tableName = mapping.TableName.Name;
    }

    // Using 0 as the default so that inserts "just work"
    public int Revision { get; set; } = 0;

    public bool IgnoreConcurrencyViolation { get; set; }

    public TId Id => _id;

    public object Document => _document;

    public IChangeTracker ToTracker(IMartenSession session)
    {
        return new ChangeTracker<T>(session, _document);
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var groupedParameters = builder.CreateGroupedParameterBuilder(',');
        // this is gross
        ConfigureParameters(groupedParameters, builder, _document, session);
    }

    public Type DocumentType => typeof(T);

    public virtual void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // Nothing
    }

    public virtual Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public abstract OperationRole Role();

    public abstract NpgsqlDbType DbType();

    public abstract void ConfigureParameters(IGroupedParameterBuilder parameterBuilder, ICommandBuilder builder, T document, IMartenSession session);

    protected void setVersionParameter(IGroupedParameterBuilder builder)
    {
        var parameter = builder.AppendParameter(_version);
        parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
    }

    protected void storeVersion()
    {
        _versions[_id] = _version;
    }

    protected void setCurrentVersionParameter(IGroupedParameterBuilder builder)
    {
        if (_versions.TryGetValue(_id, out var version))
        {
            var parameter = builder.AppendParameter(version);
            parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
        }
        else
        {
            var parameter = builder.AppendParameter<object>(DBNull.Value);
            parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
        }
    }

    protected void setCurrentRevisionParameter(IGroupedParameterBuilder builder)
    {
        var parameter = builder.AppendParameter(Revision);
        parameter.NpgsqlDbType = NpgsqlDbType.Integer;
    }

    protected bool postprocessConcurrency(DbDataReader reader, IList<Exception> exceptions)
    {
        var success = false;
        if (reader.Read())
        {
            var version = reader.GetFieldValue<Guid>(0);
            success = version == _version;
        }

        checkVersions(exceptions, success);

        return success;
    }

    protected bool postprocessRevision(DbDataReader reader, IList<Exception> exceptions)
    {
        if (IgnoreConcurrencyViolation) return true;

        var success = true;
        if (reader.Read())
        {
            var revision = reader.GetFieldValue<int>(0);
            if (revision == 0)
            {
                exceptions.Add(new ConcurrencyException(typeof(T), _id));
                return false;
            }
            if (Revision != 0) // don't care about zero or 1
            {
                if (revision >= Revision)
                {
                    exceptions.Add(new ConcurrencyException(typeof(T), _id));
                    success = false;
                }
                else
                {
                    success = true;
                }
            }

            Revision = revision;
        }

        return success;
    }

    protected async Task<bool> postprocessRevisionAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        if (IgnoreConcurrencyViolation) return true;

        var success = true;
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            var revision = await reader.GetFieldValueAsync<int>(0, token).ConfigureAwait(false);
            if (revision == 0)
            {
                exceptions.Add(new ConcurrencyException(typeof(T), _id));
                return false;
            }
            if (Revision != 0) // don't care about zero or 1
            {
                if (revision > Revision)
                {
                    exceptions.Add(new ConcurrencyException(typeof(T), _id));
                    success = false;
                }
                else
                {
                    success = true;
                }
            }

            Revision = revision;
        }

        return success;
    }

    protected void postprocessUpdate(DbDataReader reader, IList<Exception> exceptions)
    {
        if (!reader.Read() || reader.IsDBNull(0))
        {
            exceptions.Add(new NonExistentDocumentException(typeof(T), _id));
        }
    }

    protected async Task postprocessUpdateAsync(DbDataReader reader, IList<Exception> exceptions,
        CancellationToken token)
    {
        if (!await reader.ReadAsync(token).ConfigureAwait(false) || await reader.IsDBNullAsync(0, token).ConfigureAwait(false))
        {
            exceptions.Add(new NonExistentDocumentException(typeof(T), _id));
        }
    }

    protected async Task<bool> postprocessConcurrencyAsync(DbDataReader reader, IList<Exception> exceptions,
        CancellationToken token)
    {
        var success = false;
        if (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            try
            {
                var version = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
                success = version == _version;
            }
            catch (InvalidCastException)
            {
                // This is an edge case that only happens when someone calls Insert(), then Update() on the same
                // document in the same session
                success = false;
            }
        }

        checkVersions(exceptions, success);

        return success;
    }

    private void checkVersions(IList<Exception> exceptions, bool success)
    {
        if (success)
        {
            storeVersion();
        }
        else
        {
            exceptions.Add(new ConcurrencyException(typeof(T), _id));
        }
    }

    public bool TryTransform(Exception original, out Exception transformed)
    {
        transformed = null;

        if (original is MartenCommandException m)
        {
            original = m.InnerException;
        }

        if (original is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } postgresException &&
            postgresException.TableName == _tableName)
        {
            transformed = new DocumentAlreadyExistsException(original, typeof(T), _id);
            return true;
        }

        return false;
    }

    protected void setStringParameter(IGroupedParameterBuilder builder, string value)
    {
        if (value == null)
        {
            var parameter = builder.AppendParameter<object>(DBNull.Value);
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
        }
        else
        {
            var parameter = builder.AppendParameter(value);
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
        }
    }

    protected void setHeaderParameter(IGroupedParameterBuilder builder, IMartenSession session)
    {
        if (session.Headers == null)
        {
            var parameter = builder.AppendParameter<object>(DBNull.Value);
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
        }
        else
        {
            var parameter = builder.AppendParameter(session.Serializer.ToJson(session.Headers));
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
        }
    }
}
