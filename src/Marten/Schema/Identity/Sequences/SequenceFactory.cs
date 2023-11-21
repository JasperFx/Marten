#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Marten.Storage;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Marten.Schema.Identity.Sequences;

public class SequenceFactory: ISequences
{
    private readonly IMartenDatabase _database;
    private readonly StoreOptions _options;
    private readonly ConcurrentDictionary<string, ISequence> _sequences = new();

    public SequenceFactory(StoreOptions options, IMartenDatabase database)
    {
        _options = options;
        _database = database;
    }

    public string Name { get; } = "mt_hilo";

    public IEnumerable<Type> DependentTypes()
    {
        yield break;
    }

    public ISchemaObject[] Objects
    {
        get
        {
            var table = new Table(new PostgresqlObjectName(_options.DatabaseSchemaName, "mt_hilo"));
            table.AddColumn<string>("entity_name").AsPrimaryKey();
            table.AddColumn<long>("hi_value").DefaultValue(0L);

            var function = new SystemFunction(_options, "mt_get_next_hi", "varchar");

            return new ISchemaObject[] { table, function };
        }
    }

    public Type StorageType { get; } = typeof(SequenceFactory);
    public string Identifier { get; } = "hilo";

    Migrator IFeatureSchema.Migrator => _options.Advanced.Migrator;

    public void WritePermissions(Migrator rules, TextWriter writer)
    {
        // Nothing
    }

    public ISequence SequenceFor(Type documentType)
    {
        return Hilo(documentType,
            _options.Storage.MappingFor(documentType).HiloSettings ?? _options.Advanced.HiloSequenceDefaults);
    }

    public ISequence Hilo(Type documentType, HiloSettings settings)
    {
        return _sequences.GetOrAdd(GetSequenceName(documentType, settings),
            sequence => new HiloSequence(_database, _options, sequence, settings));
    }

    public bool IsActive(StoreOptions options)
    {
        return true;
    }

    private string GetSequenceName(Type documentType, HiloSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.SequenceName))
        {
            return settings.SequenceName!;
        }

        return documentType.Name;
    }

    public override string ToString()
    {
        return "Hilo Sequence Factory";
    }
}
