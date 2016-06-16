// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Linq.Expressions;
using System.Reflection;

namespace NuGet.Server.DataServices
{
    public class NormalizeVersionInterceptor : ExpressionVisitor
    {
        private static readonly MemberInfo _versionMember = typeof(ODataPackage).GetProperty("Version");
        private static readonly MemberInfo _normalizedVersionMember = typeof(ODataPackage).GetProperty("NormalizedVersion");

        protected override Expression VisitBinary(BinaryExpression node)
        {
            // Change equality comparisons on Version to normalized comparisons on NormalizedVersion
            if (node.NodeType == ExpressionType.Equal)
            {
                // Figure out which side is the target
                ConstantExpression constSide = (node.Left as ConstantExpression) ?? (node.Right as ConstantExpression);
                if (constSide != null && constSide.Type == typeof(string))
                {
                    MemberExpression memberSide = (node.Right as MemberExpression) ?? (node.Left as MemberExpression);
                    if (memberSide != null && memberSide.Member == _versionMember)
                    {
                        // We have a "Package.Version == <constant>" expression!

                        // Transform the constant version into a normalized version
                        SemanticVersion semanticVersion;
                        if (SemanticVersion.TryParse((string) constSide.Value, out semanticVersion))
                        {
                            // Create a new expression that checks the new constant against NormalizedVersion instead
                            return Expression.MakeBinary(
                                ExpressionType.Equal,
                                left: Expression.Constant(semanticVersion.ToNormalizedString()),
                                right: Expression.MakeMemberAccess(memberSide.Expression, _normalizedVersionMember));
                        }
                    }
                }
            }
            return node;
        }
    }
}