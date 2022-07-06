using System;
using System.Linq.Expressions;
using Baseline;
using Marten.Internal;
using Marten.Linq.Fields;
using Marten.Linq.Includes;
using Marten.Linq.Parsing;
using Marten.Linq.QueryHandlers;
using Marten.Linq.Selectors;
using Weasel.Postgresql;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Linq.SqlGeneration
{
    internal abstract class SelectorStatement: Statement
    {
        protected SelectorStatement(ISelectClause selectClause, IFieldMapping fields) : base(fields)
        {
            SelectClause = selectClause;
            FromObject = SelectClause.FromObject;
        }

        public ISelectClause SelectClause { get; internal set; }

        protected override void configure(CommandBuilder sql)
        {
            startCommonTableExpression(sql);

            SelectClause.WriteSelectClause(sql);

            writeWhereClause(sql);

            writeOrderClause(sql);

            if (Offset > 0)
            {
                sql.Append(" OFFSET ");
                sql.AppendParameter(Offset);
            }

            if (Limit > 0)
            {
                sql.Append(" LIMIT ");
                sql.AppendParameter(Limit);
            }

            endCommonTableExpression(sql);
        }



        public bool IsDistinct { get; set; }

        public void ToAny()
        {
            SelectClause = new AnySelectClause(SelectClause.FromObject);
            Limit = 1;
        }

        public void ToCount<T>()
        {
            SelectClause = new CountClause<T>(SelectClause.FromObject);
        }

        public IQueryHandler<TResult> BuildSingleResultHandler<TResult>(IMartenSession session, Statement topStatement)
        {
            var selector = (ISelector<TResult>)SelectClause.BuildSelector(session);
            return new OneResultHandler<TResult>(topStatement, selector, ReturnDefaultWhenEmpty, CanBeMultiples);
        }

        public void ToScalar(Expression selectClauseSelector)
        {
            var field = Fields.FieldFor(selectClauseSelector);

            if (field.FieldType == typeof(string))
            {
                SelectClause = new ScalarStringSelectClause(field, SelectClause.FromObject);
            }
            else if (field.FieldType.IsSimple() || field.FieldType == typeof(Guid) || field.FieldType == typeof(Decimal) || field.FieldType == typeof(DateTimeOffset))
            {
                SelectClause = typeof(ScalarSelectClause<>).CloseAndBuildAs<ISelectClause>(field, SelectClause.FromObject, field.FieldType);
            }
            else
            {
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, field.RawLocator, field.FieldType);
            }
        }

        public SelectorStatement ToSelectMany(IField collectionField, IMartenSession session, bool isComplex,
            Type elementType)
        {
            if (elementType.IsSimple())
            {
                var selection = $"jsonb_array_elements_text({collectionField.JSONBLocator})";

                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                Mode = StatementMode.CommonTableExpression;
                ExportName = session.NextTempTableName() + "CTE";

                var next = elementType == typeof(string)
                    ? new ScalarSelectManyStringStatement(this)
                    : typeof(ScalarSelectManyStatement<>).CloseAndBuildAs<Statement>(this, session.Serializer, elementType);

                InsertAfter(next);

                return (SelectorStatement) next;
            }

            var childFields = session.Options.ChildTypeMappingFor(elementType);

            if (isComplex)
            {
                var selection = $"jsonb_array_elements({collectionField.JSONBLocator})";
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                Mode = StatementMode.CommonTableExpression;
                ExportName = session.NextTempTableName() + "CTE";

                var statement = new JsonStatement(elementType, childFields, this);

                InsertAfter(statement);

                return statement;

            }
            else
            {
                var selection = $"jsonb_array_elements_text({collectionField.JSONBLocator})";
                SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, selection,
                    elementType);

                return this;
            }
        }

        public void ApplySqlOperator(string databaseOperator)
        {
            if (SelectClause is IScalarSelectClause c)
            {
                c.ApplyOperator(databaseOperator);

                // Hack, but let it go
                if (databaseOperator == "AVG")
                {
                    SelectClause = c.CloneToDouble();
                }
            }
            else
            {
                throw new NotSupportedException($"The database operator '{databaseOperator}' cannot be used with non-simple types");
            }
        }

        public void ApplyAggregateOperator(string databaseOperator)
        {
            ApplySqlOperator(databaseOperator);
            SingleValue = true;
            ReturnDefaultWhenEmpty = true;
        }

        public void ToSelectTransform(
            Expression selectExpression,
            MainFromClause mainFromClause,
            ISerializer serializer)
        {
            var builder = new SelectTransformBuilder(selectExpression, Fields, mainFromClause, serializer);
            var transformField = builder.SelectedFieldExpression;

            SelectClause = typeof(DataSelectClause<>).CloseAndBuildAs<ISelectClause>(SelectClause.FromObject, transformField, selectExpression.Type);
        }


        public void UseStatistics(QueryStatistics statistics)
        {
            SelectClause = SelectClause.UseStatistics(statistics);
        }

        public virtual SelectorStatement UseAsEndOfTempTableAndClone(IncludeIdentitySelectorStatement includeIdentitySelectorStatement)
        {
            throw new NotSupportedException();
        }
    }
}
