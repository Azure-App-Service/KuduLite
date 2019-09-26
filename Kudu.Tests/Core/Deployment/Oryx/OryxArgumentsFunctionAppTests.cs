using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Oryx;
using System.Collections.Generic;
using Xunit;

namespace Kudu.Tests.Core.Deployment.Oryx
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class OryxArgumentsFunctionAppTests
    {
        [Fact]
        public void DefaultTest()
        {
            IOryxArguments args = new FunctionAppOryxArguments();
            Assert.False(args.RunOryxBuild);
            Assert.False(args.SkipKuduSync);
            Assert.Equal(BuildOptimizationsFlags.None, args.Flags);

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };
            string command = args.GenerateOryxBuildCommand(mockedContext);
            Assert.Equal("oryx build OutputPath -o OutputPath -i BuildTempPath", command);
        }

        [Theory]
        [InlineData("NODE", true, false, BuildOptimizationsFlags.None)]
        [InlineData("PYTHON", true, false, BuildOptimizationsFlags.None)]
        [InlineData("PHP", true, false, BuildOptimizationsFlags.None)]
        [InlineData("DOTNET", true, false, BuildOptimizationsFlags.None)]
        public void ArgumentPropertyTest(string functions_worker_runtime,
            bool expectedRunOryxBuild, bool expectedSkipKuduSync, BuildOptimizationsFlags expectedFlags)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FUNCTIONS_WORKER_RUNTIME", functions_worker_runtime },
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new FunctionAppOryxArguments();
                Assert.Equal(expectedRunOryxBuild, args.RunOryxBuild);
                Assert.Equal(expectedSkipKuduSync, args.SkipKuduSync);
                Assert.Equal(expectedFlags, args.Flags);
            }
        }
        
        [Theory]
        [InlineData("NODE",
            "oryx build OutputPath -o OutputPath --platform nodejs --platform-version 8.15 -i BuildTempPath")]
        [InlineData("PYTHON",
            "oryx build OutputPath -o OutputPath --platform python --platform-version 3.6 -i BuildTempPath " + 
            "-p packagedir=.python_packages\\lib\\python3.6\\site-packages")]
        [InlineData("PHP",
            "oryx build OutputPath -o OutputPath --platform php --platform-version 7.3 -i BuildTempPath")]
        [InlineData("DOTNET",
            "oryx build OutputPath -o OutputPath --platform dotnet --platform-version 2.2 -i BuildTempPath")]
        public void CommandGenerationTest(string functions_worker_runtime, string expected_command)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FUNCTIONS_WORKER_RUNTIME", functions_worker_runtime },
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new FunctionAppOryxArguments();
                string command = args.GenerateOryxBuildCommand(mockedContext);
                Assert.Equal(expected_command, command);
            }
        }

        [Fact]
        public void SkipKuduSyncOnExpressBuildTest()
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FUNCTIONS_WORKER_RUNTIME", "NODE" },
                { "BUILD_FLAGS", "UseExpressBuild" }
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath", 
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath" // Should be ignored
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new FunctionAppOryxArguments();
                string command = args.GenerateOryxBuildCommand(mockedContext);
                Assert.True(args.RunOryxBuild);
                Assert.True(args.SkipKuduSync);
                Assert.Equal("oryx build RepositoryPath -o /tmp/build/expressbuild --platform nodejs --platform-version 8.15 -i BuildTempPath",
                    command);
            }
        }

        [Theory]
        [InlineData("mcr.microsoft.com/azure-functions/python:2.0-python3.7-appservice", "3.7")]
        [InlineData("mcr.microsoft.com/azure-functions/python:2.0-python3.6-appservice", "3.6")]
        [InlineData("mcr.microsoft.com/azure-functions/python:2.0-node8-appservice", "8")]
        [InlineData("mcr.microsoft.com/azure-functions/python:2.0-node10-appservice", "10")]
        [InlineData("mcr.microsoft.com/azure-functions/python:2.0-dotnet-appservice", null)]
        [InlineData("mcr.microsoft.com/azure-functions/python:2.0-dotnet2.0-appservice", "2.0")]
        [InlineData("mcr.microsoft.com/azure-functions/python:2.0-dotnet3.0-appservice", "3.0")]
        public void TestVersionFromImage(string imageName, string version)
        {
            Assert.Equal(version, FunctionAppOryxArguments.ParseRuntimeVersionFromImage(imageName));
        }
    }
}
