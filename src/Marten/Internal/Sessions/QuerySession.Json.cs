#nullable enable
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Util;
using Weasel.Postgresql;

namespace Marten.Internal.Sessions;

public partial class QuerySession
{
    public IJsonLoader Json => new JsonLoader(this);

    public async Task<bool> StreamJsonOne<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, Stream destination,
        CancellationToken token = default)
    {
        var source = _store.GetCompiledQuerySourceFor(query, this);
        var handler = (IQueryHandler<TOut>)source.Build(query, this);
        return await StreamJson(handler, destination, token).ConfigureAwait(false) > 0;
    }

    public Task<int> StreamJsonMany<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query, Stream destination,
        CancellationToken token = default)
    {
        var source = _store.GetCompiledQuerySourceFor(query, this);
        var handler = (IQueryHandler<TOut>)source.Build(query, this);
        return StreamJson(handler, destination, token);
    }

    public async Task<string?> ToJsonOne<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        CancellationToken token = default)
    {
        var stream = new MemoryStream();
        var count = await StreamJsonOne(query, stream, token).ConfigureAwait(false);
        if (!count)
        {
            return null;
        }

        stream.Position = 0;
        return await stream.ReadAllTextAsync().ConfigureAwait(false);
    }

    public async Task<string> ToJsonMany<TDoc, TOut>(ICompiledQuery<TDoc, TOut> query,
        CancellationToken token = default)
    {
        var stream = new MemoryStream();
        await StreamJsonOne(query, stream, token).ConfigureAwait(false);
        stream.Position = 0;
        return await stream.ReadAllTextAsync().ConfigureAwait(false);
    }

    public Task<int> StreamJson<T>(Stream destination, CancellationToken token, string sql, params object[] parameters)
    {
        return StreamJson<T>(destination, token, DefaultParameterPlaceholder, sql, parameters);
    }

    public Task<int> StreamJson<T>(Stream destination, CancellationToken token, char placeholder, string sql, params object[] parameters)
    {
        assertNotDisposed();
        var handler = new UserSuppliedQueryHandler<T>(this, placeholder, sql, parameters);
        var builder = new CommandBuilder();
        handler.ConfigureCommand(builder, this);
        return StreamMany(builder.Compile(), destination, token);
    }

    public Task<int> StreamJson<T>(Stream destination, string sql, params object[] parameters)
    {
        return StreamJson<T>(destination, CancellationToken.None, sql, parameters);
    }

    public Task<int> StreamJson<T>(Stream destination, char placeholder, string sql, params object[] parameters)
    {
        return StreamJson<T>(destination, CancellationToken.None, placeholder, sql, parameters);
    }

    public async Task<int> StreamJson<T>(IQueryHandler<T> handler, Stream destination, CancellationToken token)
    {
        var cmd = this.BuildCommand(handler);

        await using var reader = await ExecuteReaderAsync(cmd, token).ConfigureAwait(false);
        return await handler.StreamJson(destination, reader, token).ConfigureAwait(false);
    }
}
