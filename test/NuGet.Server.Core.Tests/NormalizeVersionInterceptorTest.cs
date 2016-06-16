// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using NuGet.Server.Core.DataServices;
using Xunit;

namespace NuGet.Server.Core.Tests
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

        [Fact]
        public void FindsPackagesUsingNormalizedVersion()
        {
            // Arrange
            var data = new List<ODataPackage>();
            data.Add(new ODataPackage { Id = "foo", Version = "1.0.0.0.0.0", NormalizedVersion = "1.0.0"});

            var queryable = data.AsQueryable().InterceptWith(new NormalizeVersionInterceptor());

            // Act
            var result1 = queryable.FirstOrDefault(p => p.Version == "1.0");
            var result2 = queryable.FirstOrDefault(p => p.Version == "1.0.0");
            var result3 = queryable.FirstOrDefault(p => p.Version == "1.0.0.0");

            // Assert
            Assert.Equal(result1, data[0]);
            Assert.Equal(result2, data[0]);
            Assert.Equal(result3, data[0]);
        }
    }
}