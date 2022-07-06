using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Baseline;
using Marten.Linq.Fields;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.Expressions;
using Remotion.Linq.Parsing;

namespace Marten.Linq.Parsing
{
    internal class SelectTransformBuilder : RelinqExpressionVisitor
    {
        private readonly MainFromClause _mainFromClause;
        private TargetObject _target;
        private SelectedField _currentField;

        public SelectTransformBuilder(
            Expression clause,
            IFieldMapping fields,
            MainFromClause mainFromClause,
            ISerializer serializer)
        {
            _mainFromClause = mainFromClause;

            // ReSharper disable once VirtualMemberCallInConstructor
            Visit(@clause);
            SelectedFieldExpression = _target.ToSelectField(fields, serializer);
        }

        public string SelectedFieldExpression { get; }

        protected override Expression VisitNew(NewExpression expression)
        {
            if (_target != null)
                return base.VisitNew(expression);

            _target = new TargetObject(expression.Type);

            var parameters = expression.Constructor.GetParameters();

            for (var i = 0; i < parameters.Length; i++)
            {
                _currentField = _target.StartBinding(parameters[i].Name);
                Visit(expression.Arguments[i]);
            }

            return expression;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            _currentField.Add(node.Member);
            return base.VisitMember(node);
        }

        protected override MemberBinding VisitMemberBinding(MemberBinding node)
        {
            _currentField = _target.StartBinding(node.Member.Name);

            return base.VisitMemberBinding(node);
        }

        protected override Expression VisitSubQuery(SubQueryExpression expression)
        {
            var autoCorrelate = true;

            var subQueryFromExpression = expression.QueryModel.MainFromClause.FromExpression;
            if (subQueryFromExpression is not MemberExpression { Expression: QuerySourceReferenceExpression querySourceReferenceExpression })
            {
                throw new NotSupportedException(
                    "subQueryFromExpression should be a MemberExpression which contains a QuerySourceReferenceExpression.");
            }

            if (querySourceReferenceExpression.ReferencedQuerySource != _mainFromClause)
            {
                autoCorrelate = false;
            }

            if (querySourceReferenceExpression.ReferencedQuerySource
                is not MainFromClause { FromExpression: ConstantExpression { Value: IMartenLinqQueryable martenLinqQueryable } })
            {
                throw new NotSupportedException(
                    "ReferencedQuerySource should be a MainFromClause referencing a constant MartenQueryable instance");
            }

            Expression newSubQueryExpression = null;

            var correlatedQueryModelBuilder = new QueryModelBuilder();
            correlatedQueryModelBuilder.AddClause(_mainFromClause);
            correlatedQueryModelBuilder.AddClause(new SelectClause(subQueryFromExpression));
            // TODO: Where clause to match the subquery id with the outer query id

            foreach (var bodyClause in expression.QueryModel.BodyClauses)
            {
                correlatedQueryModelBuilder.AddClause(bodyClause);
            }

            foreach (var resultOperator in expression.QueryModel.ResultOperators)
            {
                correlatedQueryModelBuilder.AddResultOperator(resultOperator);
            }

            // TODO LinqHandlerBuilder needs to be refactored to extract out the Sql building logic
            // and make it operate on a ready-parsed Model directly.
            var subQueryBuilder = new LinqHandlerBuilder(
                martenLinqQueryable.MartenProvider,
                martenLinqQueryable.Session,
                newSubQueryExpression);

            return base.VisitSubQuery(expression);
        }

        public class TargetObject
        {
            private readonly IList<SetterBinding> _setters = new List<SetterBinding>();

            public TargetObject(Type type)
            {
                Type = type;
            }

            public Type Type { get; }

            public SelectedField StartBinding(string bindingName)
            {
                var setter = new SetterBinding(bindingName);
                _setters.Add(setter);

                return setter.Field;
            }

            public string ToSelectField(IFieldMapping fields, ISerializer serializer)
            {
                var jsonBuildObjectArgs = _setters.Select(x => x.ToJsonBuildObjectPair(fields, serializer)).Join(", ");
                return $"jsonb_build_object({jsonBuildObjectArgs})";
            }

            private class SetterBinding
            {
                public SetterBinding(string name)
                {
                    Name = name;
                }

                private string Name { get; }
                public SelectedField Field { get; } = new SelectedField();

                public string ToJsonBuildObjectPair(IFieldMapping mapping, ISerializer serializer)
                {
                    var field = mapping.FieldFor(Field.ToArray());
                    var locator = serializer.ValueCasting == ValueCasting.Relaxed
                        ? field.RawLocator ?? field.TypedLocator
                        : field.TypedLocator;

                    if (field is DictionaryField)
                    {
                        // DictionaryField.RawLocator does not have cast to JSONB so TypedLocator is used
                        locator = field.TypedLocator;
                    }

                    return $"'{Name}', {locator}";
                }
            }
        }

        public class SelectedField: IEnumerable<MemberInfo>
        {
            private readonly Stack<MemberInfo> _members = new Stack<MemberInfo>();

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public IEnumerator<MemberInfo> GetEnumerator()
            {
                return _members.GetEnumerator();
            }

            public void Add(MemberInfo member)
            {
                _members.Push(member);
            }
        }
    }
}
