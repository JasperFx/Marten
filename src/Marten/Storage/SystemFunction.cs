using System.IO;
using Marten.Schema;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Functions;

namespace Marten.Storage;

internal class SystemFunction: Function
{
    private readonly string _args;

    public SystemFunction(StoreOptions options, string functionName, string args, bool isRemoved = false)
        : this(options.DatabaseSchemaName, functionName, args, isRemoved)
    {
    }

    public SystemFunction(string schema, string functionName, string args, bool isRemoved = false)
        : base(new PostgresqlObjectName(schema, functionName))
    {
        IsRemoved = isRemoved;
        _args = args;

        Name = functionName;
    }

    public string Name { get; }

    public override void WriteCreateStatement(Migrator rules, TextWriter writer)
    {
        var body = SchemaBuilder.GetSqlScript(Identifier.Schema, Identifier.Name);

        writer.WriteLine(body);
        writer.WriteLine("");
    }
}
