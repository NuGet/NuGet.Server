// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using NuGet.Server.DataServices;
using Xunit;

namespace NuGet.Server.Tests
{
    public class NormalizeVersionInterceptorTest
    {
        private static readonly MemberInfo _versionMember = typeof(ODataPackage).GetProperty("Version");
        private static readonly MemberInfo _normalizedVersionMember = typeof(ODataPackage).GetProperty("NormalizedVersion");

        public static IEnumerable<object[]> TheoryData
        {
            get
            {
                return new[]
                {
                    new object[]
                    {
                        Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.Constant("1.0.0.0"),
                            Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _versionMember)),

                        Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.Constant("1.0.0"),
                            Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _normalizedVersionMember))
                    },

                    new object[]
                    {
                        Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _versionMember),
                            Expression.Constant("1.0.0.0")),

                        Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.Constant("1.0.0"),
                            Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _normalizedVersionMember))
                    }
                };
            }
        }

        [Theory]
        [MemberData("TheoryData")]
        public void RewritesVersionPropertyNameToNormalizedVersionPropertyName(Expression originalExpression, Expression expectedExpression)
        {
            // Arrange
            var interceptor = new NormalizeVersionInterceptor();

            // Act
            var rewrittenExpression = interceptor.Visit(originalExpression);

            // Assert
            Assert.Equal(rewrittenExpression.ToString(), expectedExpression.ToString());
        }
    }
}