// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using NuGet.Server.Core.DataServices;
using Xunit;

namespace NuGet.Server.Core.Tests
{
    public class IgnoreCaseForPackageIdInterceptorTest
    {
        private static readonly MemberInfo _idMember = typeof(ODataPackage).GetProperty("Id");
        private static readonly Expression<Func<string, string, int>> _ordinalIgnoreCaseComparer = (a, b) => StringComparer.OrdinalIgnoreCase.Compare(a, b);

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
                            Expression.Constant("NEWTONSOFT.JSON"),
                            Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _idMember)),

                        Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.Invoke(_ordinalIgnoreCaseComparer,
                                Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _idMember),
                                Expression.Constant("NEWTONSOFT.JSON")
                            ),
                            Expression.Constant(0))
                    },

                    new object[]
                    {
                        Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _idMember),
                            Expression.Constant("NEWTONSOFT.JSON")),

                        Expression.MakeBinary(
                            ExpressionType.Equal,
                            Expression.Invoke(_ordinalIgnoreCaseComparer,
                                Expression.MakeMemberAccess(Expression.Parameter(typeof(ODataPackage)), _idMember),
                                Expression.Constant("NEWTONSOFT.JSON")
                            ),
                            Expression.Constant(0))
                    }
                };
            }
        }

        [Theory]
        [MemberData("TheoryData")]
        public void RewritesIdComparisonToIgnoreCaseComparison(Expression originalExpression, Expression expectedExpression)
        {
            // Arrange
            var interceptor = new IgnoreCaseForPackageIdInterceptor();

            // Act
            var rewrittenExpression = interceptor.Visit(originalExpression);

            // Assert
            Assert.Equal(rewrittenExpression.ToString(), expectedExpression.ToString());
        }

        [Fact]
        public void FindsPackagesIgnoringCase()
        {
            // Arrange
            var data = new List<ODataPackage>();
            data.Add(new ODataPackage { Id = "foo" });
            data.Add(new ODataPackage { Id = "BAR" });
            data.Add(new ODataPackage { Id = "bAz" });

            var queryable = data.AsQueryable().InterceptWith(new IgnoreCaseForPackageIdInterceptor());

            // Act
            var result1 = queryable.FirstOrDefault(p => p.Id == "foo");
            var result2 = queryable.FirstOrDefault(p => p.Id == "FOO");
            var result3 = queryable.FirstOrDefault(p => p.Id == "Foo");

            var result4 = queryable.FirstOrDefault(p => p.Id == "bar");
            var result5 = queryable.FirstOrDefault(p => p.Id == "BAR");
            var result6 = queryable.FirstOrDefault(p => p.Id == "baR");

            var result7 = queryable.FirstOrDefault(p => p.Id == "baz");
            var result8 = queryable.FirstOrDefault(p => p.Id == "BAZ");
            var result9 = queryable.FirstOrDefault(p => p.Id == "bAz");

            // Assert
            Assert.Equal(result1.Id, data[0].Id);
            Assert.Equal(result2.Id, data[0].Id);
            Assert.Equal(result3.Id, data[0].Id);

            Assert.Equal(result4.Id, data[1].Id);
            Assert.Equal(result5.Id, data[1].Id);
            Assert.Equal(result6.Id, data[1].Id);

            Assert.Equal(result7.Id, data[2].Id);
            Assert.Equal(result8.Id, data[2].Id);
            Assert.Equal(result9.Id, data[2].Id);
        }
    }
}