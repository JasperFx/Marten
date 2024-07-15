#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core.Exceptions;
using Marten.Exceptions;
using Marten.Services;
using Marten.Storage;
using Npgsql;

namespace Marten.Internal.Sessions;

internal class AutoClosingLifetime: ConnectionLifetimeBase, IConnectionLifetime, ITransactionStarter
{
    private readonly SessionOptions _options;
    private readonly IMartenDatabase _database;

    public AutoClosingLifetime(SessionOptions options, StoreOptions storeOptions)
    {
        if (options.Tenant == null)
            throw new ArgumentOutOfRangeException(nameof(options), "Tenant.Database cannot be null");

        _options = options;
        _database = options.Tenant!.Database;

        CommandTimeout = _options.Timeout ?? storeOptions.CommandTimeout;
    }

    public int CommandTimeout { get; }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {

    }


    public int Execute(NpgsqlCommand cmd)
    {
        Logger.OnBeforeExecute(cmd);
        using var conn = _database.CreateConnection();
        conn.Open();
        try
        {
            cmd.Connection = conn;
            cmd.CommandTimeout = CommandTimeout;
            var returnValue = cmd.ExecuteNonQuery();

            Logger.LogSuccess(cmd);
            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(cmd, e);
            throw;
        }
        finally
        {
            conn.Close();
        }
    }

    public async Task<int> ExecuteAsync(NpgsqlCommand command,
        CancellationToken token = new())
    {
        Logger.OnBeforeExecute(command);
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            Logger.LogSuccess(command);

            return returnValue;
        }
        catch (Exception e)
        {
            handleCommandException(command, e);
            throw;
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public DbDataReader ExecuteReader(NpgsqlCommand command)
    {
        Logger.OnBeforeExecute(command);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection(ConnectionUsage.Read);
        conn.Open();

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = command.ExecuteReader(CommandBehavior.CloseConnection);
            Logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
            conn.Close();
            handleCommandException(command, e);
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlCommand command, CancellationToken token = default)
    {
        Logger.OnBeforeExecute(command);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection(ConnectionUsage.Read);
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            command.Connection = conn;
            command.CommandTimeout = CommandTimeout;
            var returnValue = await command.ExecuteReaderAsync(CommandBehavior.CloseConnection, token).ConfigureAwait(false);
            Logger.LogSuccess(command);
            return returnValue;
        }
        catch (Exception e)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            handleCommandException(command, e);
            throw;
        }
    }

    public DbDataReader ExecuteReader(NpgsqlBatch batch)
    {
        Logger.OnBeforeExecute(batch);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection(ConnectionUsage.Read);
        conn.Open();

        try
        {
            batch.Connection = conn;
            batch.Timeout = CommandTimeout;
            var reader = batch.ExecuteReader(CommandBehavior.CloseConnection);
            Logger.LogSuccess(batch);
            return reader;
        }
        catch (Exception)
        {
            conn.Close();
            throw;
        }
    }

    public async Task<DbDataReader> ExecuteReaderAsync(NpgsqlBatch batch, CancellationToken token = default)
    {
        Logger.OnBeforeExecute(batch);

        // Do NOT use a using block here because we're returning the reader
        var conn = _database.CreateConnection(ConnectionUsage.Read);
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            batch.Connection = conn;
            batch.Timeout = CommandTimeout;
            var reader = await batch.ExecuteReaderAsync(CommandBehavior.CloseConnection, token).ConfigureAwait(false);

            Logger.LogSuccess(batch);

            return reader;
        }
        catch (Exception)
        {
            await conn.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    public void ExecuteBatchPages(IReadOnlyList<OperationPage> pages, List<Exception> exceptions)
    {
        using var conn = _database.CreateConnection();
        conn.Open();

        try
        {
            var tx = conn.BeginTransaction();

            try
            {
                foreach (var page in pages)
                {
                    var batch = page.Compile();
                    batch.Timeout = CommandTimeout;
                    batch.Connection = conn;
                    batch.Transaction = tx;

                    Logger.OnBeforeExecute(batch);
                    try
                    {
                        using var reader = batch.ExecuteReader();
                        page.ApplyCallbacks(reader, exceptions);
                        reader.Close();
                    }
                    catch (Exception e)
                    {
                        Logger.LogFailure(batch, e);
                        throw;
                    }

                    Logger.LogSuccess(batch);
                }
            }
            catch (Exception e)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception rollbackEx)
                {
                    Logger.LogFailure(rollbackEx, "Error trying to rollback an exception");
                    throw;
                }

                Logger.LogFailure(new NpgsqlCommand(), e);
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>()
                    .Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (exceptions.Count == 1)
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception e)
                {
                    Logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                var ex = exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (exceptions.Any())
            {
                try
                {
                    tx.Rollback();
                }
                catch (Exception e)
                {
                    Logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                throw new AggregateException(exceptions);
            }

            tx.Commit();
        }
        finally
        {
            conn.Close();
        }
    }

    public async Task ExecuteBatchPagesAsync(IReadOnlyList<OperationPage> pages, List<Exception> exceptions,
        CancellationToken token)
    {
        await using var conn = _database.CreateConnection();
        await conn.OpenAsync(token).ConfigureAwait(false);

        try
        {
            var tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false);

            try
            {
                foreach (var page in pages)
                {
                    var batch = page.Compile();
                    batch.Timeout = CommandTimeout;
                    batch.Connection = conn;
                    batch.Transaction = tx;

                    Logger.OnBeforeExecute(batch);
                    try
                    {
                        await using var reader = await batch.ExecuteReaderAsync(token).ConfigureAwait(false);
                        await page.ApplyCallbacksAsync(reader, exceptions, token).ConfigureAwait(false);
                        await reader.CloseAsync().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Logger.LogFailure(batch, e);
                        throw;
                    }

                    Logger.LogSuccess(batch);
                }
            }
            catch (Exception e)
            {
                try
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                }
                catch (Exception rollbackEx)
                {
                    Logger.LogFailure(rollbackEx, "Error trying to rollback an exception");
                    throw;
                }

                Logger.LogFailure(new NpgsqlCommand(), e);
                pages.SelectMany(x => x.Operations).OfType<IExceptionTransform>()
                    .Concat(MartenExceptionTransformer.Transforms).TransformAndThrow(e);
            }

            if (exceptions.Count == 1)
            {
                try
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                var ex = exceptions.Single();
                ExceptionDispatchInfo.Throw(ex);
            }

            if (exceptions.Any())
            {
                try
                {
                    await tx.RollbackAsync(token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Logger.LogFailure(e, "Failure trying to rollback an exception");
                }

                throw new AggregateException(exceptions);
            }

            await tx.CommitAsync(token).ConfigureAwait(false);
        }
        finally
        {
            await conn.CloseAsync().ConfigureAwait(false);
        }
    }

    public IAlwaysConnectedLifetime Start()
    {
        var transaction = _options.Mode == CommandRunnerMode.ReadOnly
            ? new ReadOnlyTransactionalConnection(_options){Logger = Logger, CommandTimeout = CommandTimeout}
            : new TransactionalConnection(_options){Logger = Logger, CommandTimeout = CommandTimeout};
        transaction.BeginTransaction();

        return transaction;
    }

    public async Task<IAlwaysConnectedLifetime> StartAsync(CancellationToken token)
    {
        var transaction = _options.Mode == CommandRunnerMode.ReadOnly
            ? new ReadOnlyTransactionalConnection(_options){Logger = Logger, CommandTimeout = CommandTimeout}
            : new TransactionalConnection(_options){Logger = Logger, CommandTimeout = CommandTimeout };

        await transaction.BeginTransactionAsync(token).ConfigureAwait(false);

        return transaction;
    }
}
