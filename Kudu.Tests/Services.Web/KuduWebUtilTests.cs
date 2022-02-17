using System.Collections.Generic;
using Kudu.Contracts.Infrastructure;
using Kudu.Core.Tracing;
using Kudu.Services.Web;
using Xunit;

namespace Kudu.Tests.Services
{
    //File delete cannot be finished immediately
    public class KuduWebUtilTests
    {
        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithPerAppEnv_False_False()
        {
            TestNamedSiteLockWithPerAppEnv("FALSE", "FALSE");
            TestNamedSiteLockWithPerAppEnv("false", "False");
            TestNamedSiteLockSuccess("false", "false");
        }

        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithPerAppEnv_False_True()
        {
            TestNamedSiteLockWithPerAppEnv("FALSE", "True");
            TestNamedSiteLockWithPerAppEnv("false", "TRUE");
            TestNamedSiteLockSuccess("false", "TRUE");
        }

        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithPerAppEnv_True_False()
        {
            TestNamedSiteLockWithPerAppEnv("true", "FALSE");
            TestNamedSiteLockWithPerAppEnv("TRUE", "False");
            TestNamedSiteLockSuccess("TRUE", "False");
        }

        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithPerAppEnv_True_True()
        {
            TestNamedSiteLockWithPerAppEnv("TRUE", "TRUE");
            TestNamedSiteLockWithPerAppEnv("True", "true");
            TestNamedSiteLockSuccess("True", "true");
        }

        private void TestNamedSiteLockWithPerAppEnv(string isBuildJob, string useBuildJob)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "IS_BUILD_JOB", isBuildJob},
                { "USE_BUILD_JOB",useBuildJob }
            };

            bool usePerSiteLock = string.Equals(isBuildJob, "true", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(useBuildJob, "true", System.StringComparison.OrdinalIgnoreCase);

            ITraceFactory traceFactory = NullTracerFactory.Instance;
            var environment1 = TestMockedEnvironment.GetMockedEnvironment(appName: "testApp");
            var environment2 = TestMockedEnvironment.GetMockedEnvironment(appName: "testApp2");
            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                var deploymentLock = KuduWebUtil.GetDeploymentLock(traceFactory, environment1);

                deploymentLock.LockOperation(() =>
                {
                    var deploymentLock2 = KuduWebUtil.GetDeploymentLock(traceFactory, environment2);
                    var lockResult = deploymentLock2.TryLockOperation(() =>
                    {
                        
                    }, "deploymentLockAgain", new System.TimeSpan(0, 0, 2));

                    Assert.True(usePerSiteLock && lockResult || !usePerSiteLock && !lockResult);

                }, "deploymentLock", new System.TimeSpan(0, 0, 30));
            }
        }

        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithGlobalEnv_False_False()
        {
            TestNamedSiteLockWithGlobalEnv("FALSE", "FALSE");
            TestNamedSiteLockWithGlobalEnv("false", "False");
            TestNamedSiteLockSuccess("false", "false", true);
        }

        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithGlobalEnv_False_True()
        {
            TestNamedSiteLockWithGlobalEnv("FALSE", "True");
            TestNamedSiteLockWithGlobalEnv("false", "TRUE");
            TestNamedSiteLockSuccess("false", "TRUE", true);
        }

        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithGlobalEnv_True_False()
        {
            TestNamedSiteLockWithGlobalEnv("true", "FALSE");
            TestNamedSiteLockWithGlobalEnv("TRUE", "False");
            TestNamedSiteLockSuccess("TRUE", "False", true);
        }

        [Fact(Skip = "only for local run")]
        public void TestNamedSiteLockWithGlobalEnv_True_True()
        {
            TestNamedSiteLockWithGlobalEnv("TRUE", "TRUE");
            TestNamedSiteLockWithGlobalEnv("True", "true");
            TestNamedSiteLockSuccess("True", "true", true);
        }

        private void TestNamedSiteLockWithGlobalEnv(string isBuildJob, string useBuildJob)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "IS_BUILD_JOB", isBuildJob},
                { "USE_BUILD_JOB",useBuildJob }
            };

            bool usePerSiteLock = string.Equals(isBuildJob, "true", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(useBuildJob, "true", System.StringComparison.OrdinalIgnoreCase);

            ITraceFactory traceFactory = NullTracerFactory.Instance;
            var environment1 = TestMockedEnvironment.GetMockedEnvironment();
            var environment2 = TestMockedEnvironment.GetMockedEnvironment();
            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                var deploymentLock = KuduWebUtil.GetDeploymentLock(traceFactory, environment1);

                deploymentLock.LockOperation(() =>
                {
                    var deploymentLock2 = KuduWebUtil.GetDeploymentLock(traceFactory, environment2);
                    var lockResult = deploymentLock2.TryLockOperation(() =>
                    {

                    }, "deploymentLockAgain", new System.TimeSpan(0, 0, 2));

                    Assert.False(lockResult);

                }, "deploymentLock", new System.TimeSpan(0, 0, 30));
            }
        }

        private void TestNamedSiteLockSuccess(string isBuildJob, string useBuildJob, bool global = false)
        {
            var mockedEnvironment = new Dictionary<string, string>()
            {
                { "IS_BUILD_JOB", isBuildJob},
                { "USE_BUILD_JOB",useBuildJob }
            };

            ITraceFactory traceFactory = NullTracerFactory.Instance;
            var environment1 = TestMockedEnvironment.GetMockedEnvironment(appName: global ? null : "testApp");
            var environment2 = TestMockedEnvironment.GetMockedEnvironment(appName: global ? null : "testApp2");
            using (new TestScopedEnvironmentVariable(mockedEnvironment))
            {
                var deploymentLock = KuduWebUtil.GetDeploymentLock(traceFactory, environment1);

                deploymentLock.LockOperation(() =>
                {
                    var deploymentLock2 = KuduWebUtil.GetDeploymentLock(traceFactory, environment2);
                    var lockResult = deploymentLock.TryLockOperation(() =>
                    {
                    }, "deploymentLockAgain", new System.TimeSpan(0, 0, 10));

                    Assert.True(lockResult);

                }, "deploymentLock", new System.TimeSpan(0, 0, 3));
            }
        }
    }
}