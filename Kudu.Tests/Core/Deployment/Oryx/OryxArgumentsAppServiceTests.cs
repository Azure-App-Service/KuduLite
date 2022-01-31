using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Oryx;
using System.Collections.Generic;
using Xunit;

namespace Kudu.Tests.Core.Deployment.Oryx
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class OryxArgumentsAppServiceTests
    {
        [Fact]
        public void DefaultTest()
        {
            IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
            Assert.False(args.RunOryxBuild);
            Assert.False(args.SkipKuduSync);
            Assert.Equal(BuildOptimizationsFlags.Off, args.Flags);

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };
            string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
            Assert.Equal("oryx build OutputPath -o OutputPath", command);
        }

        [Theory]
        [InlineData("NODE", "8.15", true, false, BuildOptimizationsFlags.CompressModules)]
        [InlineData("PYTHON", "3.6", true, false, BuildOptimizationsFlags.CompressModules)]
        [InlineData("PHP", "7.3", true, false, BuildOptimizationsFlags.None)]
        [InlineData("DOTNETCORE", "3.1", true, true, BuildOptimizationsFlags.UseTempDirectory)]
        public void ArgumentPropertyTest(string language, string version,
            bool expectedRunOryxBuild, bool expectedSkipKuduSync, BuildOptimizationsFlags expectedFlags)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FRAMEWORK", language },
                { "FRAMEWORK_VERSION", version}
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedRunOryxBuild, args.RunOryxBuild);
                Assert.Equal(expectedSkipKuduSync, args.SkipKuduSync);
                Assert.Equal(expectedFlags, args.Flags);
            }
        }

        [Theory]
        [InlineData("NODE", "8.12",
            "oryx build RepositoryPath -o BuildTempPath --platform nodejs -i BuildTempPath -p compress_node_modules=tar-gz")]
        [InlineData("PYTHON", "3.6",
            "oryx build RepositoryPath -o BuildTempPath --platform python --platform-version 3.6 -i BuildTempPath -p compress_virtualenv=tar-gz -p virtualenv_name=antenv3.6")]
        [InlineData("PHP", "7.3",
            "oryx build RepositoryPath -o OutputPath --platform php")]
        [InlineData("DOTNETCORE", "3.1",
            "oryx build RepositoryPath -o OutputPath --platform dotnet --platform-version 3.1 -i BuildTempPath")]
        public void CommandGenerationTest(string language, string version, string expectedCommand)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FRAMEWORK", language },
                { "FRAMEWORK_VERSION", version }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedCommand, args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment()));
            }
        }

        [Theory]
        [InlineData("1.0", "oryx build RepositoryPath -o OutputPath --platform dotnet --platform-version 1.1 -i BuildTempPath")]
        [InlineData("2.0", "oryx build RepositoryPath -o OutputPath --platform dotnet --platform-version 2.1 -i BuildTempPath")]
        public void DotnetcoreVersionPromotionTest(string version, string expectedCommand)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FRAMEWORK", "DOTNETCORE" },
                { "FRAMEWORK_VERSION", version }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedCommand, args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment()));
            }
        }

        [Fact]
        public void NodejsUseExpressBuildTest()
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FRAMEWORK", "NODE" },
                { "FRAMEWORK_VERSION", "8.12" },
                { "BUILD_FLAGS", "UseExpressBuild" }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal("oryx build RepositoryPath -o BuildTempPath --platform nodejs -i BuildTempPath",
                    args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment()));
            }
        }

        [Theory]
        [InlineData("2.7", "oryx build RepositoryPath -o BuildTempPath --platform python --platform-version 2.7 -i BuildTempPath -p compress_virtualenv=tar-gz -p virtualenv_name=antenv2.7")]
        [InlineData("3.6", "oryx build RepositoryPath -o BuildTempPath --platform python --platform-version 3.6 -i BuildTempPath -p compress_virtualenv=tar-gz -p virtualenv_name=antenv3.6")]
        [InlineData("3.7", "oryx build RepositoryPath -o BuildTempPath --platform python --platform-version 3.7 -i BuildTempPath -p compress_virtualenv=tar-gz -p virtualenv_name=antenv")]
        public void PythonVirtualEnvironmentNamingTest(string version, string expectedCommand)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FRAMEWORK", "PYTHON" },
                { "FRAMEWORK_VERSION", version }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedCommand, args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment()));
            }
        }

        [Theory]
        [InlineData("NODE", "oryx build OutputPath -o OutputPath")]
        [InlineData("PYTHON", "oryx build OutputPath -o OutputPath")]
        [InlineData("PHP", "oryx build OutputPath -o OutputPath")]
        [InlineData("DOTNETCORE", "oryx build OutputPath -o OutputPath")]
        public void MissingFrameworkVersionEnvironmentTest(string language, string expectedCommand)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FRAMEWORK", language },
                //{ "FRAMEWORK_VERSION", version }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedCommand, args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment()));
            }
        }

        [Fact]
        public void MissingFrameworkEnvironmentTest()
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                //{ "FRAMEWORK", language },
                { "FRAMEWORK_VERSION", "3.6" }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal("oryx build OutputPath -o OutputPath", args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment()));
            }
        }

        [Fact]
        public void MissingOutputPathTest()
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FRAMEWORK", "PYTHON" },
                { "FRAMEWORK_VERSION", "3.6" }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new AppServiceOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal("oryx build RepositoryPath -o BuildTempPath --platform python --platform-version 3.6 -i BuildTempPath -p compress_virtualenv=tar-gz -p virtualenv_name=antenv3.6",
                    args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment()));
            }
        }
    }
}
