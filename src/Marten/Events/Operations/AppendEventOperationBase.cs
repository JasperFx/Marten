using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Weasel.Postgresql;

#nullable enable

namespace Marten.Events.Operations
{
    // Leave public for codegen!
    public abstract class AppendEventOperationBase : IStorageOperation
    {
        public StreamAction Stream { get; }
        public IEvent Event { get; }

        public AppendEventOperationBase(StreamAction stream, IEvent e)
        {
            Stream = stream;
            Event = e;
        }

        public override string ToString()
        {
            return $"Insert Event to Stream {Stream.Key ?? Stream.Id.ToString()}, Version {Event.Version}";
        }

        public abstract void ConfigureCommand(CommandBuilder builder, IMartenSession session);

        public Type DocumentType => typeof(IEvent);
        public string? TenantId => Stream.TenantId;

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
            return OperationRole.Events;
        }
    }
}
