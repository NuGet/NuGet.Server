using NuGet.Server.Core.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using Moq;
using NuGet.Server.Core.DataServices;

namespace NuGet.Server.V2.Tests.Infrastructure
{
    public class ControllerTestHelpers
    {
        public static Mock<IServerPackageRepository> SetupTestPackageRepository()
        {
            //var fooPackage = new PackageRegistration { Id = "Foo" };
            //var barPackage = new PackageRegistration { Id = "Bar" };
            //var bazPackage = new PackageRegistration { Id = "Baz" };

            var repo = new Mock<IServerPackageRepository>(MockBehavior.Strict);
            repo.Setup(r => r.GetPackages()).Returns(new[]
            {
                new ServerPackage
                {
                    //PackageRegistration = fooPackage,
                    Id="Foo",
                    Version = SemanticVersion.Parse("1.0.0"),                  
                    //IsPrerelease = false,
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Foo",
                    Summary = "Foo",
                    Tags = "Foo CommonTag"
                },
                new ServerPackage
                {
                    //PackageRegistration = fooPackage,
                    Id="Foo",
                    Version = SemanticVersion.Parse("1.0.1-a"),
                    //IsPrerelease = true,
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Foo",
                    Summary = "Foo",
                    Tags = "Foo CommonTag"
                },
                new ServerPackage
                {
                    //PackageRegistration = barPackage,
                    Id = "Bar",
                    Version = SemanticVersion.Parse("1.0.0"),
                    //IsPrerelease = false,
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    //PackageRegistration = barPackage,
                    Id = "Bar",
                    Version = SemanticVersion.Parse("2.0.0"),
                    //IsPrerelease = false,
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    //PackageRegistration = barPackage,
                    Id = "Bar",
                    Version = SemanticVersion.Parse("2.0.1-a"),
                    //IsPrerelease = true,
                    Listed = true,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    //PackageRegistration = barPackage,
                    Id = "Bar",
                    Version = SemanticVersion.Parse("2.0.1-b"),
                    //IsPrerelease = true,
                    Listed = false,
                    Authors = new [] { "Test" },
                    Owners = new [] { "Test" },
                    Description = "Bar",
                    Summary = "Bar",
                    Tags = "Bar CommonTag"
                },
                new ServerPackage
                {
                    //PackageRegistration = bazPackage,
                    Id = "Baz",
                    Version = SemanticVersion.Parse("1.0.0"),
                    //IsPrerelease = false,
                    Listed = false,
                    //Deleted = true, // plot twist: this package is a soft-deleted one
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
