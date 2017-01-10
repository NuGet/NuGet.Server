// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Threading;
using Moq;
using NuGet.Server.Core.Infrastructure;

namespace NuGet.Server.V2.Tests.Infrastructure
{
    public class ControllerTestHelpers
    {
        public static Mock<IServerPackageRepository> SetupTestPackageRepository()
        {
            var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
            repo.Setup(r => r.GetPackagesAsync(
                It.IsAny<ClientCompatibility>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(new[]
            {
                new ServerPackage
                {
                    Id="Foo",
                    Version = SemanticVersion.Parse("1.0.0"),
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Foo",
                    Summary = "Foo",
                    Tags = "Foo CommonTag"
                },
                new ServerPackage
                {
                    Id="Foo",
                    Version = SemanticVersion.Parse("1.0.1-a"),
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Foo",
                    Summary = "Foo",
                    Tags = "Foo CommonTag"
                },
                new ServerPackage
                {
                    Id = "Bar",
                    Version = SemanticVersion.Parse("1.0.0"),
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    Id = "Bar",
                    Version = SemanticVersion.Parse("2.0.0"),
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    Id = "Bar",
                    Version = SemanticVersion.Parse("2.0.1-a"),
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    Id = "Bar",
                    Version = SemanticVersion.Parse("2.0.1-b"),
                    Listed = false,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    Id = "Baz",
                    Version = SemanticVersion.Parse("2.0.1-b.1"),
                    Listed = false,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Baz",
                    Summary = "Baz",
                    Tags = "Baz CommonTag"
                },
                new ServerPackage
                {
                    Id = "Baz",
                    Version = SemanticVersion.Parse("2.0.2-b+git"),
                    Listed = false,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Baz",
                    Summary = "Baz",
                    Tags = "Baz CommonTag"
                },
                new ServerPackage
                {
                    Id = "Baz",
                    Version = SemanticVersion.Parse("2.0.2+git"),
                    Listed = false,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Baz",
                    Summary = "Baz",
                    Tags = "Baz CommonTag"
                },
                new ServerPackage
                {
                    Id = "Baz",
                    Version = SemanticVersion.Parse("1.0.0"),
                    Listed = false,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Baz",
                    Summary = "Baz",
                    Tags = "Baz CommonTag"
                }
            }.AsQueryable());

            return repo;
        }

    }
}
