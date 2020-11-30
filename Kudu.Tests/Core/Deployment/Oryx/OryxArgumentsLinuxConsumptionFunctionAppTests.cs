using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Oryx;
using System.Collections.Generic;
using Xunit;

namespace Kudu.Tests.Core.Deployment.Oryx
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class OryxArgumentsLinuxConsumptionFunctionAppTests
    {
        [Fact]
        public void DefaultTest()
        {
            IOryxArguments args = new LinuxConsumptionFunctionAppOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
            Assert.False(args.RunOryxBuild);
            Assert.True(args.SkipKuduSync);
            Assert.Equal(BuildOptimizationsFlags.Off, args.Flags);

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath", // Should be ignored
                OutputPath = "OutputPath"
            };
            string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
            Assert.Equal("oryx build RepositoryPath -o RepositoryPath", command);
        }

        [Theory]
        [InlineData("NODE", true, true, BuildOptimizationsFlags.Off)]
        [InlineData("PYTHON", true, true, BuildOptimizationsFlags.Off)]
        [InlineData("PHP", true, true, BuildOptimizationsFlags.Off)]
        [InlineData("DOTNET", true, true, BuildOptimizationsFlags.Off)]
        public void ArgumentPropertyTest(string functions_worker_runtime,
            bool expectedRunOryxBuild, bool expectedSkipKuduSync, BuildOptimizationsFlags expectedFlags)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FUNCTIONS_WORKER_RUNTIME", functions_worker_runtime },
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new LinuxConsumptionFunctionAppOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedRunOryxBuild, args.RunOryxBuild);
                Assert.Equal(expectedSkipKuduSync, args.SkipKuduSync);
                Assert.Equal(expectedFlags, args.Flags);
            }
        }

        [Theory]
        [InlineData("NODE",
            "oryx build RepositoryPath -o RepositoryPath --platform nodejs --platform-version 8.15")]
        [InlineData("PYTHON",
            "oryx build RepositoryPath -o RepositoryPath --platform python --platform-version 3.6 " +
            "-p packagedir=.python_packages\\lib\\python3.6\\site-packages")]
        [InlineData("PHP",
            "oryx build RepositoryPath -o RepositoryPath --platform php --platform-version 7.3")]
        [InlineData("DOTNET",
            "oryx build RepositoryPath -o RepositoryPath --platform dotnet --platform-version 2.2")]
        public void CommandGenerationTest(string functions_worker_runtime, string expected_command)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "FUNCTIONS_WORKER_RUNTIME", functions_worker_runtime },
            };

            var mockedContext = new DeploymentContext()
            {
                RepositoryPath = "RepositoryPath",
                BuildTempPath = "BuildTempPath", // Should be ignored
                OutputPath = "OutputPath" // Should be ignored
            };

            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                IOryxArguments args = new LinuxConsumptionFunctionAppOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(mockedContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expected_command, command);
            }
        }
    }
}
