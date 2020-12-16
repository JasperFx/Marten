using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Internal.CompiledQueries
{
    public abstract class ComplexCompiledQuery<TOut, TQuery> : IQueryHandler<TOut>
    {
        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public abstract IQueryHandler<TOut> BuildHandler(IMartenSession session);

        public TOut Handle(DbDataReader reader, IMartenSession session)
        {
            var inner = BuildHandler(session);
            return inner.Handle(reader, session);
        }

        public Task<TOut> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            var inner = BuildHandler(session);
            return inner.HandleAsync(reader, session, token);
        }

        protected string StartsWith(string value)
        {
            return $"%{value}";
        }

        protected string ContainsString(string value)
        {
            return $"%{value}%";
        }

        protected string EndsWith(string value)
        {
            return $"{value}%";
        }
    }
}
