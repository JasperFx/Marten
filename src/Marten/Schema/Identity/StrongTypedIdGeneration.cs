using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FastExpressionCompiler;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Internal;
using Marten.Linq;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Marten.Linq.SqlGeneration;
using Marten.Storage;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Schema.Identity;

public class StrongTypedIdGeneration: IIdGeneration
{
    private readonly MethodInfo _builder;
    private readonly ConstructorInfo _ctor;
    private readonly IScalarSelectClause _selector;

    private StrongTypedIdGeneration(Type idType, PropertyInfo innerProperty, Type simpleType, ConstructorInfo ctor)
    {
        InnerProperty = innerProperty;
        _ctor = ctor;
        IdType = idType;
        SimpleType = simpleType;

        _selector = typeof(StrongTypedIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, IdType, SimpleType);
    }

    private StrongTypedIdGeneration(Type idType, PropertyInfo innerProperty, Type simpleType, MethodInfo builder)
    {
        IdType = idType;
        InnerProperty = innerProperty;
        _builder = builder;
        SimpleType = simpleType;

        _selector = typeof(StrongTypedIdSelectClause<,>).CloseAndBuildAs<IScalarSelectClause>(this, IdType, SimpleType);
    }

    public Type IdType { get; }
    public Type SimpleType { get; }

    public PropertyInfo InnerProperty { get; }

    public IEnumerable<Type> KeyTypes => Type.EmptyTypes;
    public bool RequiresSequences => false;

    public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);

        if (SimpleType == typeof(Guid))
        {
            generateGuidWrapper(method, mapping, document);
        }
        else if (SimpleType == typeof(int))
        {
            generateIntWrapper(method, mapping, document);
        }
        else if (SimpleType == typeof(long))
        {
            generateLongWrapper(method, mapping, document);
        }
        else if (SimpleType == typeof(string))
        {
            generateStringWrapper(method, mapping, document);
        }
        else
        {
            throw new NotSupportedException();
        }

        method.Frames.Code($"return {{0}}.{mapping.CodeGen.AccessId};", document);
    }

    private void generateStringWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        method.Frames.Code($"return {{0}}.{mapping.IdMember.Name}.Value;", document);
    }

    private void generateLongWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        var database = Use.Type<IMartenDatabase>();
        if (_ctor != null)
        {
            method.Frames.Code(
                $"if ({{0}}.{mapping.IdMember.Name} == null) _setter({{0}}, new {IdType.FullNameInCode()}({{1}}.Sequences.SequenceFor({{2}}).NextLong()));",
                document, database, mapping.DocumentType);
        }
        else
        {
            method.Frames.Code(
                $"if ({{0}}.{mapping.IdMember.Name} == null) _setter({{0}}, {IdType.FullNameInCode()}.{_builder.Name}({{1}}.Sequences.SequenceFor({{2}}).NextLong()));",
                document, database, mapping.DocumentType);
        }
    }

    private void generateIntWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        var database = Use.Type<IMartenDatabase>();
        if (_ctor != null)
        {
            method.Frames.Code(
                $"if ({{0}}.{mapping.IdMember.Name} == null) _setter({{0}}, new {IdType.FullNameInCode()}({{1}}.Sequences.SequenceFor({{2}}).NextInt()));",
                document, database, mapping.DocumentType);
        }
        else
        {
            method.Frames.Code(
                $"if ({{0}}.{mapping.IdMember.Name} == null) _setter({{0}}, {IdType.FullNameInCode()}.{_builder.Name}({{1}}.Sequences.SequenceFor({{2}}).NextInt()));",
                document, database, mapping.DocumentType);
        }
    }

    private void generateGuidWrapper(GeneratedMethod method, DocumentMapping mapping, Use document)
    {
        var newGuid = $"{typeof(CombGuidIdGeneration).FullNameInCode()}.NewGuid()";
        var create = _ctor == null
            ? $"{IdType.FullNameInCode()}.{_builder.Name}({newGuid})"
            : $"new {IdType.FullNameInCode()}({newGuid})";

        method.Frames.Code(
            $"if ({{0}}.{mapping.IdMember.Name} == null) _setter({{0}}, {create});",
            document);
    }

    public ISelectClause BuildSelectClause(string tableName)
    {
        return _selector.CloneToOtherTable(tableName);
    }

    public static bool IsCandidate(Type idType, out IIdGeneration? idGeneration)
    {
        if (idType.IsNullable())
        {
            idType = idType.GetGenericArguments().Single();
        }

        idGeneration = default;
        if (idType.IsClass)
        {
            return false;
        }

        if (!idType.Name.EndsWith("Id"))
        {
            return false;
        }

        if (!idType.IsPublic && !idType.IsNestedPublic)
        {
            return false;
        }

        var properties = idType.GetProperties().Where(x => DocumentMapping.ValidIdTypes.Contains(x.PropertyType))
            .ToArray();
        if (properties.Length == 1)
        {
            var innerProperty = properties[0];
            var identityType = innerProperty.PropertyType;

            var ctor = idType.GetConstructors().FirstOrDefault(x =>
                x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == identityType);

            var dbType = PostgresqlProvider.Instance.GetDatabaseType(identityType, EnumStorage.AsInteger);
            var parameterType = PostgresqlProvider.Instance.TryGetDbType(identityType);

            if (ctor != null)
            {
                PostgresqlProvider.Instance.RegisterMapping(idType, dbType, parameterType);
                idGeneration = new StrongTypedIdGeneration(idType, innerProperty, identityType, ctor);
                return true;
            }

            var builder = idType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x =>
                    x.ReturnType == idType && x.GetParameters().Length == 1 &&
                    x.GetParameters()[0].ParameterType == identityType);

            if (builder != null)
            {
                PostgresqlProvider.Instance.RegisterMapping(idType, dbType, parameterType);
                idGeneration = new StrongTypedIdGeneration(idType, innerProperty, identityType, builder);
                return true;
            }
        }


        return false;
    }

    public string ParameterValue(DocumentMapping mapping)
    {
        if (mapping.IdMember.GetRawMemberType().IsNullable())
        {
            return $"{mapping.IdMember.Name}.Value.{InnerProperty.Name}";
        }

        return $"{mapping.IdMember.Name}.{InnerProperty.Name}";
    }

    public void GenerateCodeForFetchingId(int index, GeneratedMethod sync, GeneratedMethod async,
        DocumentMapping mapping)
    {
        if (_builder != null)
        {
            sync.Frames.Code(
                $"var id = {IdType.FullNameInCode()}.{_builder.Name}(reader.GetFieldValue<{SimpleType.FullNameInCode()}>({index}));");
            async.Frames.CodeAsync(
                $"var id = {IdType.FullNameInCode()}.{_builder.Name}(await reader.GetFieldValueAsync<{SimpleType.FullNameInCode()}>({index}, token));");
        }
        else
        {
            sync.Frames.Code(
                $"var id = new {IdType.FullNameInCode()}(reader.GetFieldValue<{SimpleType.FullNameInCode()}>({index}));");
            async.Frames.CodeAsync(
                $"var id = new {IdType.FullNameInCode()}(await reader.GetFieldValueAsync<{SimpleType.FullNameInCode()}>({index}, token));");
        }
    }

    public Func<object, T> BuildInnerValueSource<T>()
    {
        var target = Expression.Parameter(typeof(object), "target");
        var method = InnerProperty.GetMethod;

        var callGetMethod = Expression.Call(Expression.Convert(target, IdType), method);

        var lambda = Expression.Lambda<Func<object, T>>(callGetMethod, target);

        return lambda.CompileFast();
    }


    public Func<TInner, TOuter> CreateConverter<TOuter, TInner>()
    {
        var inner = Expression.Parameter(typeof(TInner), "inner");
        Expression builder;
        if (_builder != null)
        {
            builder = Expression.Call(null, _builder, inner);
        }
        else if (_ctor != null)
        {
            builder = Expression.New(_ctor, inner);
        }
        else
        {
            throw new NotSupportedException("Marten cannot build a type converter for strong typed id type " +
                                            IdType.FullNameInCode());
        }

        var lambda = Expression.Lambda<Func<TInner, TOuter>>(builder, inner);

        return lambda.CompileFast();
    }

    public void WriteBulkWriterCode(GeneratedMethod load, DocumentMapping mapping)
    {
        var dbType = PostgresqlProvider.Instance.ToParameterType(SimpleType);
        load.Frames.Code($"writer.Write(document.{mapping.IdMember.Name}.Value.{InnerProperty.Name}, {{0}});", dbType);
    }

    public void WriteBulkWriterCodeAsync(GeneratedMethod load, DocumentMapping mapping)
    {
        var dbType = PostgresqlProvider.Instance.ToParameterType(SimpleType);
        load.Frames.Code($"await writer.WriteAsync(document.{mapping.IdMember.Name}.Value.{InnerProperty.Name}, {{0}}, {{1}});", dbType, Use.Type<CancellationToken>());
    }
}

internal class StrongTypedIdSelectClause<TOuter, TInner>: ISelectClause, IScalarSelectClause, IModifyableFromObject,
    ISelector<TOuter?> where TOuter : struct
{
    public StrongTypedIdSelectClause(StrongTypedIdGeneration idGeneration)
    {
        Converter = idGeneration.CreateConverter<TOuter, TInner>();
        MemberName = "d.id";
    }

    public StrongTypedIdSelectClause(Func<TInner, TOuter> converter)
    {
        Converter = converter;
    }

    public Func<TInner, TOuter> Converter { get; }

    public string MemberName { get; set; } = "d.id";

    public ISelectClause CloneToOtherTable(string tableName)
    {
        return new StrongTypedIdSelectClause<TOuter, TInner>(Converter)
        {
            FromObject = tableName, MemberName = MemberName
        };
    }

    public void ApplyOperator(string op)
    {
        MemberName = $"{op}({MemberName})";
    }

    public ISelectClause CloneToDouble()
    {
        throw new NotSupportedException();
    }

    public Type SelectedType => typeof(TOuter);

    public string FromObject { get; set; }

    public void Apply(ICommandBuilder sql)
    {
        if (MemberName.IsNotEmpty())
        {
            sql.Append("select ");
            sql.Append(MemberName);
            sql.Append(" as data from ");
        }

        sql.Append(FromObject);
        sql.Append(" as d");
    }

    public string[] SelectFields()
    {
        return new[] { MemberName };
    }

    public ISelector BuildSelector(IMartenSession session)
    {
        return this;
    }

    public IQueryHandler<TResult> BuildHandler<TResult>(IMartenSession session, ISqlFragment statement,
        ISqlFragment currentStatement)
    {
        return (IQueryHandler<TResult>)new ListQueryHandler<TOuter?>(statement, this);
    }

    public ISelectClause UseStatistics(QueryStatistics statistics)
    {
        return new StatsSelectClause<TOuter?>(this, statistics);
    }

    public TOuter? Resolve(DbDataReader reader)
    {
        var inner = reader.GetFieldValue<TInner>(0);
        return Converter(inner);
    }

    public async Task<TOuter?> ResolveAsync(DbDataReader reader, CancellationToken token)
    {
        var inner = await reader.GetFieldValueAsync<TInner>(0, token).ConfigureAwait(false);
        return Converter(inner);
    }

    public override string ToString()
    {
        return $"Data from {FromObject}";
    }
}
