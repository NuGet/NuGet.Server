using System;
using System.Linq.Expressions;

namespace NuGet.Server.DataServices
{
    /// <summary>
    /// This class is used to replace expression 
    ///     element.Id == "packageId"
    /// with
    ///     string.Equals(element.Id, "packageId", StringComparison.OrdinalIgnoreCase)
    /// so that package id comparison is case insensitive.
    /// </summary>
    public class PackageIdComparisonVisitor : ExpressionVisitor
    {
        protected override Expression VisitBinary(BinaryExpression node)
        {
            if (node.NodeType == ExpressionType.Equal &&
                node.Left.ToString() == "element.Id")
            {
                try
                {
                    var stringEqualsMethod = typeof(string).GetMethod(
                        "Equals",
                        new[] { typeof(string), typeof(string), typeof(StringComparison) });
                    var newExpression = Expression.Call(
                        stringEqualsMethod,
                        node.Left,
                        node.Right,
                        Expression.Constant(StringComparison.OrdinalIgnoreCase));
                    return newExpression;
                }
                catch
                {
                    return base.VisitBinary(node);
                }
            }

            return base.VisitBinary(node);
        }
    }
}