// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Linq.Expressions;
using System.Reflection;

namespace NuGet.Server.DataServices
{
    public class IgnoreCaseForPackageIdInterceptor : ExpressionVisitor
    {
        private static readonly MemberInfo _idMember = typeof(ODataPackage).GetProperty("Id");
        private static readonly Expression<Func<string, string, int>> _ordinalIgnoreCaseComparer = (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a, b);
        
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
                    if (memberSide != null && memberSide.Member == _idMember)
                    {
                        // We have a "Package.Id == <constant>" expression!

                        // Rewrite it to StringComparer.OrdinalIgnoreCase.Compare(a, b) == 0
                        return Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.Invoke(_ordinalIgnoreCaseComparer,
                                Expression.MakeMemberAccess(memberSide.Expression, _idMember),
                                constSide
                            ),
                            Expression.Constant(0));
                    }
                }
            }
            return node;
        }
    }
}