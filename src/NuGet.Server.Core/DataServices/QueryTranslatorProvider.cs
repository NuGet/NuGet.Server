// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace NuGet.Server.DataServices
{
    internal abstract class QueryTranslatorProvider : ExpressionVisitor
    {
        protected QueryTranslatorProvider(IQueryable source)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }

            Source = source;
        }

        internal IQueryable Source { get; }
    }

    // ReSharper disable once UnusedTypeParameter
    internal class QueryTranslatorProvider<T> : QueryTranslatorProvider, IQueryProvider
    {
        private readonly IEnumerable<ExpressionVisitor> _visitors;

        public QueryTranslatorProvider(IQueryable source, IEnumerable<ExpressionVisitor> visitors)
            : base(source)
        {
            if (visitors == null)
            {
                throw new ArgumentNullException("visitors");
            }
            _visitors = visitors;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException("expression");
            }

            return new QueryTranslator<TElement>(Source, expression, _visitors);
        }

        public IQueryable CreateQuery(Expression expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException("expression");
            }

            var elementType = expression.Type.GetGenericArguments().First();
            var result = (IQueryable)Activator.CreateInstance(typeof(QueryTranslator<>).MakeGenericType(elementType), Source, expression, _visitors);
            return result;
        }

        public TResult Execute<TResult>(Expression expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException("expression");
            }
            var result = (this as IQueryProvider).Execute(expression);
            return (TResult)result;
        }

        public object Execute(Expression expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException("expression");
            }

            var translated = VisitAll(expression);
            return Source.Provider.Execute(translated);
        }

        internal IEnumerable ExecuteEnumerable(Expression expression)
        {
            if (expression == null)
            {
                throw new ArgumentNullException("expression");
            }

            var translated = VisitAll(expression);
            return Source.Provider.CreateQuery(translated);
        }

        private Expression VisitAll(Expression expression)
        {
            // Run all visitors in order
            var visitors = new ExpressionVisitor[] { this }.Concat(_visitors);

            return visitors.Aggregate(expression, (expr, visitor) => visitor.Visit(expr));
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            // Fix up the Expression tree to work with the underlying LINQ provider
            if (node.Type.IsGenericType &&
                node.Type.GetGenericTypeDefinition() == typeof(QueryTranslator<>))
            {

                var provider = ((IQueryable)node.Value).Provider as QueryTranslatorProvider;

                if (provider != null)
                {
                    return provider.Source.Expression;
                }

                return Source.Expression;
            }

            return base.VisitConstant(node);
        }
    }
}
