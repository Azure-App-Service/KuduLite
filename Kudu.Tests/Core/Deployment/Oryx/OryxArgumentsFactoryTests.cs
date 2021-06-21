using Kudu.Core.Deployment;
using Kudu.Core.Deployment.Oryx;
using System;
using System.Collections.Generic;
using Xunit;

namespace Kudu.Tests.Core.Deployment.Oryx
{
    [Collection("MockedEnvironmentVariablesCollection")]
    public class OryxArgumentsFactoryTests
    {
        [Fact]
        public void OryxArgumentShouldBeAppService()
        {
            IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
            Assert.IsType<AppServiceOryxArguments>(args);
        }

        [Fact]
        public void OryxArgumentShouldBeFunctionApp()
        {
            using (new TestScopedEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "PYTHON"))
            using (new TestScopedEnvironmentVariable("FUNCTIONS_EXTENSION_VERSION", "~2"))
            {
                IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                Assert.IsType<FunctionAppOryxArguments>(args);
            }
        }

        [Fact]
        public void OryxArgumentShouldBeLinuxConsumption()
        {
            using (new TestScopedEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "PYTHON"))
            using (new TestScopedEnvironmentVariable("FUNCTIONS_EXTENSION_VERSION", "~2"))
            using (new TestScopedEnvironmentVariable("SCM_RUN_FROM_PACKAGE", "http://microsoft.com"))
            {
                IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                Assert.IsType<LinuxConsumptionFunctionAppOryxArguments>(args);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(false, "FUNCTIONS_EXTENSION_VERSION", "~2")]
        [InlineData(false, "FUNCTIONS_EXTENSION_VERSION", "~2", "SCM_RUN_FROM_PACKAGE", "http://microsoft.com")]
        [InlineData(true, "FUNCTIONS_EXTENSION_VERSION", "~2", "FUNCTIONS_WORKER_RUNTIME", "PYTHON")]
        [InlineData(true, "FUNCTIONS_EXTENSION_VERSION", "~2", "SCM_RUN_FROM_PACKAGE", "http://microsoft.com", "FUNCTIONS_WORKER_RUNTIME", "PYTHON")]
        public void OryxArgumentRunOryxBuild(bool expectedRunOryxBuild, params string[] varargs)
        {
            IDictionary<string, string> env = new Dictionary<string, string>();
            for (int i = 0; i < varargs.Length; i += 2)
            {
                env.Add(varargs[i], varargs[i + 1]);
            }

            using (new TestScopedEnvironmentVariable(env))
            {
                IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedRunOryxBuild, args.RunOryxBuild);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(false, "FUNCTIONS_EXTENSION_VERSION", "~2")]
        [InlineData(true, "FUNCTIONS_EXTENSION_VERSION", "~2", "SCM_RUN_FROM_PACKAGE", "http://microsoft.com")]
        [InlineData(false, "FUNCTIONS_EXTENSION_VERSION", "~2", "FUNCTIONS_WORKER_RUNTIME", "PYTHON")]
        [InlineData(true, "FUNCTIONS_EXTENSION_VERSION", "~2", "SCM_RUN_FROM_PACKAGE", "http://microsoft.com", "FUNCTIONS_WORKER_RUNTIME", "PYTHON")]
        public void OryxArgumentSkipKuduSync(bool expectedSkipKuduSync, params string[] varargs)
        {
            IDictionary<string, string> env = new Dictionary<string, string>();
            for (int i = 0; i < varargs.Length; i += 2)
            {
                env.Add(varargs[i], varargs[i + 1]);
            }

            using (new TestScopedEnvironmentVariable(env))
            {
                IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(expectedSkipKuduSync, args.SkipKuduSync);
            }
        }

        [Fact]
        public void BuildCommandForAppService()
        {
            DeploymentContext deploymentContext = new DeploymentContext()
            {
                OutputPath = "outputpath"
            };
            IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
            string command = args.GenerateOryxBuildCommand(deploymentContext, TestMockedEnvironment.GetMockedEnvironment());
            Assert.Equal(@"oryx build outputpath -o outputpath", command);
        }

        [Fact]
        public void BuildCommandForFunctionApp()
        {
            DeploymentContext deploymentContext = new DeploymentContext()
            {
                OutputPath = "outputpath",
                BuildTempPath = "buildtemppath"
            };

            using (new TestScopedEnvironmentVariable("FUNCTIONS_EXTENSION_VERSION", "~2"))
            {
                IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(deploymentContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(@"oryx build outputpath -o outputpath -i buildtemppath", command);
            }
        }

        // TODO check if it is proper change
        [Fact]
        public void BuildCommandForLinuxConsumptionFunctionApp()
        {
            DeploymentContext deploymentContext = new DeploymentContext()
            {
                RepositoryPath = "repositorypath"
            };

            using (new TestScopedEnvironmentVariable("FUNCTIONS_EXTENSION_VERSION", "~2"))
            using (new TestScopedEnvironmentVariable("SCM_RUN_FROM_PACKAGE", "http://microsoft.com"))
            {
                IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(deploymentContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(@"oryx build repositorypath -o ", command);
            }
        }

        [Fact]
        public void BuildCommandForPythonFunctionApp()
        {
            DeploymentContext deploymentContext = new DeploymentContext()
            {
                OutputPath = "outputpath",
                BuildTempPath = "buildtemppath"
            };

            using (new TestScopedEnvironmentVariable("FUNCTIONS_EXTENSION_VERSION", "~2"))
            using (new TestScopedEnvironmentVariable("FUNCTIONS_WORKER_RUNTIME", "python"))
            {
                IOryxArguments args = OryxArgumentsFactory.CreateOryxArguments(TestMockedEnvironment.GetMockedEnvironment());
                string command = args.GenerateOryxBuildCommand(deploymentContext, TestMockedEnvironment.GetMockedEnvironment());
                Assert.Equal(@"oryx build outputpath -o outputpath --platform python --platform-version 3.6 -i buildtemppath -p packagedir=.python_packages\lib\python3.6\site-packages", command);
            }
        }
    }
}
