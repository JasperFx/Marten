#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Events;

internal partial class EventStore
{
    public void BuildCommandForReadingVersionForStream(ICommandBuilder builder, Guid streamId, bool forUpdate)
    {
        builder.Append("select version from ");
        builder.Append(_store.Events.DatabaseSchemaName);
        builder.Append('.');
        builder.Append("mt_streams where id = ");
        builder.AppendParameter(streamId);

        if (_store.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        if (forUpdate)
        {
            builder.Append(" for update");
        }
    }

    public void BuildCommandForReadingVersionForStream(ICommandBuilder builder, string streamKey, bool forUpdate)
    {
        builder.Append("select version from ");
        builder.Append(_store.Events.DatabaseSchemaName);
        builder.Append('.');
        builder.Append("mt_streams where id = ");
        builder.AppendParameter(streamKey);

        if (_store.Events.TenancyStyle == TenancyStyle.Conjoined)
        {
            builder.Append(" and tenant_id = ");
            builder.AppendParameter(builder.TenantId);
        }

        if (forUpdate)
        {
            builder.Append(" for update");
        }
    }


    public async Task AppendOptimistic(string streamKey, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsStringStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var version = await readVersionFromExistingStream(streamKey, false, token).ConfigureAwait(false);

        var action = Append(streamKey, events);
        action.ExpectedVersionOnServer = version;
    }

    public Task AppendOptimistic(string streamKey, params object[] events)
    {
        return AppendOptimistic(streamKey, CancellationToken.None, events);
    }

    public async Task AppendOptimistic(Guid streamId, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsGuidStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        var version = await readVersionFromExistingStream(streamId, false, token).ConfigureAwait(false);

        var action = Append(streamId, events);
        action.ExpectedVersionOnServer = version;
    }

    public Task AppendOptimistic(Guid streamId, params object[] events)
    {
        return AppendOptimistic(streamId, CancellationToken.None, events);
    }

    public async Task AppendExclusive(string streamKey, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsStringStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        try
        {
            await _session.BeginTransactionAsync(token).ConfigureAwait(false);

            var version = await readVersionFromExistingStream(streamKey, true, token).ConfigureAwait(false);

            var action = Append(streamKey, events);
            action.ExpectedVersionOnServer = version;
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage) || e.InnerException is NpgsqlException
                {
                    SqlState: PostgresErrorCodes.InFailedSqlTransaction
                })
            {
                throw new StreamLockedException(streamKey, e.InnerException);
            }

            throw;
        }
    }

    public Task AppendExclusive(string streamKey, params object[] events)
    {
        return AppendExclusive(streamKey, CancellationToken.None, events);
    }

    public async Task AppendExclusive(Guid streamId, CancellationToken token, params object[] events)
    {
        _store.Events.EnsureAsGuidStorage(_session);
        await _session.Database.EnsureStorageExistsAsync(typeof(IEvent), token).ConfigureAwait(false);

        try
        {
            await _session.BeginTransactionAsync(token).ConfigureAwait(false);

            var version = await readVersionFromExistingStream(streamId, true, token).ConfigureAwait(false);

            var action = Append(streamId, events);
            action.ExpectedVersionOnServer = version;
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage) || e.InnerException is NpgsqlException
                {
                    SqlState: PostgresErrorCodes.InFailedSqlTransaction
                })
            {
                throw new StreamLockedException(streamId, e.InnerException);
            }

            throw;
        }
    }

    public Task AppendExclusive(Guid streamId, params object[] events)
    {
        return AppendExclusive(streamId, CancellationToken.None, events);
    }

    private async Task<long> readVersionFromExistingStream(Guid streamId, bool forUpdate, CancellationToken token)
    {
        var builder = new CommandBuilder{TenantId = _session.TenantId};
        BuildCommandForReadingVersionForStream(builder, streamId, forUpdate);

        long version = 0;
        try
        {
            await using var reader = await _session.ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(streamId, e.InnerException);
            }

            throw;
        }

        if (version == 0)
        {
            throw new NonExistentStreamException(streamId);
        }

        return version;
    }

    private async Task<long> readVersionFromExistingStream(string streamKey, bool forUpdate,
        CancellationToken token)
    {
        var builder = new CommandBuilder { TenantId = _session.TenantId };
        BuildCommandForReadingVersionForStream(builder, streamKey, forUpdate);

        long version = 0;
        try
        {
            await using var reader = await _session.ExecuteReaderAsync(builder.Compile(), token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                version = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            if (e.Message.Contains(MartenCommandException.MaybeLockedRowsMessage))
            {
                throw new StreamLockedException(streamKey, e.InnerException);
            }

            throw;
        }

        if (version == 0)
        {
            throw new NonExistentStreamException(streamKey);
        }

        return version;
    }
}
