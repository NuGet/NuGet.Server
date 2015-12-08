using System;
using System.Linq;
using System.Linq.Expressions;

namespace NuGet.Server.DataServices
{
    public static class QueryableExtensions
    {
        public static IQueryable<T> InterceptWith<T>(this IQueryable<T> source, params ExpressionVisitor[] visitors)
        {
            if (source == null)
            {
                throw new ArgumentNullException("source");
            }
            return new QueryTranslator<T>(source, visitors);
        }
    }
}
