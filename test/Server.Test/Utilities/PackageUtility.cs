using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using Moq;
using NuGet.Test.Utility;

namespace NuGet.Test
{
    public class PackageUtility
    {
        public static IPackage CreateProjectLevelPackage(string id, string version = "1.0", IEnumerable<PackageDependency> dependencies = null)
        {
            return CreatePackage(id, version, assemblyReferences: new[] { id + ".dll" }, dependencies: dependencies);
        }

        public static IPackage CreatePackage(string id,
                                              string version = "1.0",
                                              IEnumerable<string> content = null,
                                              IEnumerable<string> assemblyReferences = null,
                                              IEnumerable<string> tools = null,
                                              IEnumerable<PackageDependency> dependencies = null,
                                              int downloadCount = 0,
                                              string description = null,
                                              string summary = null,
                                              bool listed = true,
                                              string tags = "",
                                              string language = null,
                                              IEnumerable<string> satelliteAssemblies = null,
                                              string minClientVersion = null,
                                              bool createRealStream = true)
        {
            assemblyReferences = assemblyReferences ?? Enumerable.Empty<string>();
            satelliteAssemblies = satelliteAssemblies ?? Enumerable.Empty<string>();
            
            return CreatePackage(id,
                                 version,
                                 content,
                                 CreateAssemblyReferences(assemblyReferences),
                                 tools,
                                 dependencies,
                                 downloadCount,
                                 description,
                                 summary,
                                 listed,
                                 tags,
                                 language,
                                 CreateAssemblyReferences(satelliteAssemblies),
                                 minClientVersion,
                                 createRealStream);
        }

        public static IPackage CreatePackage(string id,
                                              string version,
                                              IEnumerable<string> content,
                                              IEnumerable<IPackageAssemblyReference> assemblyReferences,
                                              IEnumerable<string> tools,
                                              IEnumerable<PackageDependency> dependencies,
                                              int downloadCount,
                                              string description,
                                              string summary,
                                              bool listed,
                                              string tags,
                                              string minClientVersion = null)
        {
            return CreatePackage(id,
                                 version,
                                 content,
                                 assemblyReferences,
                                 tools,
                                 dependencies,
                                 downloadCount,
                                 description,
                                 summary,
                                 listed,
                                 tags,
                                 language: null,
                                 satelliteAssemblies: null,
                                 minClientVersion: minClientVersion);
        }

        public static IPackage CreatePackageWithDependencySets(string id,
                                              string version = "1.0",
                                              IEnumerable<string> content = null,
                                              IEnumerable<string> assemblyReferences = null,
                                              IEnumerable<string> tools = null,
                                              IEnumerable<PackageDependencySet> dependencySets = null,
                                              int downloadCount = 0,
                                              string description = null,
                                              string summary = null,
                                              bool listed = true,
                                              string tags = "",
                                              string language = null,
                                              IEnumerable<string> satelliteAssemblies = null,
                                              string minClientVersion = null,
                                              bool createRealStream = true)
        {
            assemblyReferences = assemblyReferences ?? Enumerable.Empty<string>();
            satelliteAssemblies = satelliteAssemblies ?? Enumerable.Empty<string>();

            return CreatePackage(id,
                                 version,
                                 content,
                                 CreateAssemblyReferences(assemblyReferences),
                                 tools,
                                 dependencySets,
                                 downloadCount,
                                 description,
                                 summary,
                                 listed,
                                 tags,
                                 language,
                                 CreateAssemblyReferences(satelliteAssemblies),
                                 minClientVersion,
                                 createRealStream);
        }

        public static IPackage CreatePackage(string id,
                                              string version,
                                              IEnumerable<string> content,
                                              IEnumerable<IPackageAssemblyReference> assemblyReferences,
                                              IEnumerable<string> tools,
                                              IEnumerable<PackageDependency> dependencies,
                                              int downloadCount,
                                              string description,
                                              string summary,
                                              bool listed,
                                              string tags,
                                              string language,
                                              IEnumerable<IPackageAssemblyReference> satelliteAssemblies,
                                              string minClientVersion = null,
                                              bool createRealStream = true)
        {
            var dependencySets = new List<PackageDependencySet>
            {
                new PackageDependencySet(null, dependencies ?? Enumerable.Empty<PackageDependency>())
            };

            return CreatePackage(id,
                                 version,
                                 content,
                                 assemblyReferences,
                                 tools,
                                 dependencySets,
                                 downloadCount,
                                 description,
                                 summary,
                                 listed,
                                 tags,
                                 language,
                                 satelliteAssemblies,
                                 minClientVersion,
                                 createRealStream);
        }

        // If content is null, "file1.txt" is used added as a content file.
        public static IPackage CreatePackage(string id,
                                              string version,
                                              IEnumerable<string> content,
                                              IEnumerable<IPackageAssemblyReference> assemblyReferences,
                                              IEnumerable<string> tools,
                                              IEnumerable<PackageDependencySet> dependencySets,
                                              int downloadCount,
                                              string description,
                                              string summary,
                                              bool listed,
                                              string tags,
                                              string language,
                                              IEnumerable<IPackageAssemblyReference> satelliteAssemblies,
                                              string minClientVersion = null,
                                              bool createRealStream = true)
        {
            content = content ?? new[] { "file1.txt" };
            assemblyReferences = assemblyReferences ?? Enumerable.Empty<IPackageAssemblyReference>();
            satelliteAssemblies = satelliteAssemblies ?? Enumerable.Empty<IPackageAssemblyReference>();
            dependencySets = dependencySets ?? Enumerable.Empty<PackageDependencySet>();
            tools = tools ?? Enumerable.Empty<string>();
            description = description ?? "Mock package " + id;

            var allFiles = new List<IPackageFile>();
            allFiles.AddRange(CreateFiles(content, "content"));
            allFiles.AddRange(CreateFiles(tools, "tools"));
            allFiles.AddRange(assemblyReferences);
            allFiles.AddRange(satelliteAssemblies);

            var mockPackage = new Mock<IPackage>(MockBehavior.Strict) { CallBase = true };
            mockPackage.Setup(m => m.IsAbsoluteLatestVersion).Returns(true);
            mockPackage.Setup(m => m.IsLatestVersion).Returns(String.IsNullOrEmpty(SemanticVersion.Parse(version).SpecialVersion));
            mockPackage.Setup(m => m.Id).Returns(id);
            mockPackage.Setup(m => m.Listed).Returns(true);
            mockPackage.Setup(m => m.Version).Returns(new SemanticVersion(version));
            mockPackage.Setup(m => m.GetFiles()).Returns(allFiles);
            mockPackage.Setup(m => m.AssemblyReferences).Returns(assemblyReferences);
            mockPackage.Setup(m => m.DependencySets).Returns(dependencySets);
            mockPackage.Setup(m => m.Description).Returns(description);
            mockPackage.Setup(m => m.Language).Returns("en-US");
            mockPackage.Setup(m => m.Authors).Returns(new[] { "Tester" });
            mockPackage.Setup(m => m.LicenseUrl).Returns(new Uri("ftp://test/somelicense.txts"));
            mockPackage.Setup(m => m.Summary).Returns(summary);
            mockPackage.Setup(m => m.FrameworkAssemblies).Returns(Enumerable.Empty<FrameworkAssemblyReference>());
            mockPackage.Setup(m => m.Tags).Returns(tags);
            mockPackage.Setup(m => m.Title).Returns(String.Empty);
            mockPackage.Setup(m => m.DownloadCount).Returns(downloadCount);
            mockPackage.Setup(m => m.RequireLicenseAcceptance).Returns(false);
            mockPackage.Setup(m => m.DevelopmentDependency).Returns(false);
            mockPackage.Setup(m => m.Listed).Returns(listed);
            mockPackage.Setup(m => m.Language).Returns(language);
            mockPackage.Setup(m => m.IconUrl).Returns((Uri)null);
            mockPackage.Setup(m => m.ProjectUrl).Returns((Uri)null);
            mockPackage.Setup(m => m.ReleaseNotes).Returns("");
            mockPackage.Setup(m => m.Owners).Returns(new string[0]);
            mockPackage.Setup(m => m.Copyright).Returns("");
            mockPackage.Setup(m => m.MinClientVersion).Returns(minClientVersion == null ? new Version() : Version.Parse(minClientVersion));
            mockPackage.Setup(m => m.PackageAssemblyReferences).Returns(new PackageReferenceSet[0]);
            if (!listed)
            {
                mockPackage.Setup(m => m.Published).Returns(Constants.Unpublished);
            }
            var targetFramework = allFiles.Select(f => f.TargetFramework).Where(f => f != null);
            mockPackage.Setup(m => m.GetSupportedFrameworks()).Returns(targetFramework);

            // Create the package's stream
            if (createRealStream)
            {
                PackageBuilder builder = new PackageBuilder();
                builder.Id = id;
                builder.Version = new SemanticVersion(version);
                builder.Description = description;
                builder.Authors.Add("Tester");

                foreach (var f in allFiles)
                {
                    builder.Files.Add(f);
                }
                var packageStream = new MemoryStream();
                builder.Save(packageStream);
                packageStream.Seek(0, SeekOrigin.Begin);
                mockPackage.Setup(m => m.GetStream()).Returns(packageStream);
            }
            else
            {
                mockPackage.Setup(m => m.GetStream()).Returns(new MemoryStream());
            }

            return mockPackage.Object; 
        }

        private static List<IPackageAssemblyReference> CreateAssemblyReferences(IEnumerable<string> fileNames)
        {
            var assemblyReferences = new List<IPackageAssemblyReference>();
            foreach (var fileName in fileNames)
            {
                var mockAssemblyReference = new Mock<IPackageAssemblyReference>();
                mockAssemblyReference.Setup(m => m.GetStream()).Returns(() => new MemoryStream());
                mockAssemblyReference.Setup(m => m.Path).Returns(fileName);
                mockAssemblyReference.Setup(m => m.Name).Returns(Path.GetFileName(fileName));


                string effectivePath;
                FrameworkName fn;
                try
                {
                    fn = ParseFrameworkName(fileName, out effectivePath);
                }
                catch (ArgumentException)
                {
                    effectivePath = fileName;
                    fn = VersionUtility.UnsupportedFrameworkName;
                }

                if (fn != null)
                {
                    mockAssemblyReference.Setup(m => m.EffectivePath).Returns(effectivePath);
                    mockAssemblyReference.Setup(m => m.TargetFramework).Returns(fn);
                    mockAssemblyReference.Setup(m => m.SupportedFrameworks).Returns(new[] { fn });
                }
                else
                {
                    mockAssemblyReference.Setup(m => m.EffectivePath).Returns(fileName);
                }

                assemblyReferences.Add(mockAssemblyReference.Object);
            }
            return assemblyReferences;
        }

        private static FrameworkName ParseFrameworkName(string fileName, out string effectivePath)
        {
            if (fileName.StartsWith("lib\\"))
            {
                fileName = fileName.Substring(4);
                return VersionUtility.ParseFrameworkFolderName(fileName, strictParsing: false, effectivePath: out effectivePath);
            }

            effectivePath = fileName;
            return null;
        }

        public static IPackageAssemblyReference CreateAssemblyReference(string path, FrameworkName targetFramework)
        {
            var mockAssemblyReference = new Mock<IPackageAssemblyReference>();
            mockAssemblyReference.Setup(m => m.GetStream()).Returns(() => new MemoryStream());
            mockAssemblyReference.Setup(m => m.Path).Returns(path);
            mockAssemblyReference.Setup(m => m.Name).Returns(path);
            mockAssemblyReference.Setup(m => m.TargetFramework).Returns(targetFramework);
            mockAssemblyReference.Setup(m => m.SupportedFrameworks).Returns(new[] { targetFramework });
            return mockAssemblyReference.Object;
        }

        public static List<IPackageFile> CreateFiles(IEnumerable<string> fileNames, string directory = "")
        {
            var files = new List<IPackageFile>();
            foreach (var fileName in fileNames)
            {
                var mockFile = CreateMockedPackageFile(directory, fileName);
                files.Add(mockFile.Object);
            }
            return files;
        }

        public static Mock<IPackageFile> CreateMockedPackageFile(string directory, string fileName, string content = null)
        {
            string path = PathFixUtility.FixPath(Path.Combine(directory, fileName));
            content = content ?? path;
            
            var mockFile = new Mock<IPackageFile>();
            mockFile.Setup(m => m.Path).Returns(path);
            mockFile.Setup(m => m.GetStream()).Returns(() => new MemoryStream(Encoding.Default.GetBytes(content)));

            string effectivePath;
            FrameworkName fn = VersionUtility.ParseFrameworkNameFromFilePath(path, out effectivePath);
            mockFile.Setup(m => m.TargetFramework).Returns(fn);
            mockFile.Setup(m => m.EffectivePath).Returns(effectivePath);
            mockFile.Setup(m => m.SupportedFrameworks).Returns(
                fn == null ? new FrameworkName[0] : new FrameworkName[] { fn });
            return mockFile;
        }

        public static Stream CreateSimplePackageStream(string id, string version = "1.0")
        {
            var packageBuilder = new PackageBuilder
            {
                Id = id,
                Version = SemanticVersion.Parse(version),
                Description = "Test description",
            };

            var dependencySet = new PackageDependencySet(VersionUtility.DefaultTargetFramework,
                new PackageDependency[] {
                    new PackageDependency("Foo")
                });
            packageBuilder.DependencySets.Add(dependencySet);
            packageBuilder.Authors.Add("foo");

            var memoryStream = new MemoryStream();
            packageBuilder.Save(memoryStream);
            memoryStream.Seek(0, SeekOrigin.Begin);

            return memoryStream;
        }
    }
}