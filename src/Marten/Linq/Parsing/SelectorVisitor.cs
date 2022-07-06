using System;
using System.Linq.Expressions;
using LamarCodeGeneration;

namespace Marten.Linq.Parsing
{
    internal partial class LinqHandlerBuilder
    {
        public class SelectorVisitor: ExpressionVisitor
        {
            private readonly LinqHandlerBuilder _parent;
            private ISerializer _serializer;

            public SelectorVisitor(LinqHandlerBuilder parent)
            {
                _parent = parent;
                _serializer = parent._session.Serializer;
            }

            protected override Expression VisitUnary(UnaryExpression node)
            {
                _parent.CurrentStatement.ToScalar(node);
                return null;
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                _parent.CurrentStatement.ToScalar(node);
                return null;
            }

            protected override Expression VisitMemberInit(MemberInitExpression node)
            {
                _parent.CurrentStatement.ToSelectTransform(node, _parent.Model.MainFromClause, _serializer);
                return null;
            }

            protected override Expression VisitNew(NewExpression node)
            {
                _parent.CurrentStatement.ToSelectTransform(node, _parent.Model.MainFromClause, _serializer);
                return null;
            }


        }
    }
}
