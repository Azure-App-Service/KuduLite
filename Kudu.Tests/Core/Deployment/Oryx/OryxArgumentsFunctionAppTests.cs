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
            IOryxArguments args = new FunctionAppOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
            Assert.False(args.RunOryxBuild);
            Assert.False(args.SkipKuduSync);
            Assert.Equal(BuildOptimizationsFlags.None, args.Flags);

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath",
                OutputPath = "OutputPath"
            };
            string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
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
                IOryxArguments args = new FunctionAppOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
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
            "oryx build OutputPath -o OutputPath --platform dotnet --platform-version 3.1 -i BuildTempPath")]
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
                IOryxArguments args = new FunctionAppOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
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
                IOryxArguments args = new FunctionAppOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.True(args.RunOryxBuild);
                Assert.True(args.SkipKuduSync);
                Assert.Equal("oryx build RepositoryPath -o /tmp/build/expressbuild --platform nodejs --platform-version 8.15 -i BuildTempPath",
                    command);
            }
        }
    }
}
