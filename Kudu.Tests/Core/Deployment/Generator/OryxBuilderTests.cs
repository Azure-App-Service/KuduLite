using System;
using System.Collections.Generic;
using System.Text;
using Xunit;
using Kudu.Core.Deployment.Generator;
using Kudu.Core;
using Kudu.Core.Deployment;
using Kudu.Core.Settings;
using Kudu.Contracts.Settings;
using Microsoft.AspNetCore.Http;

namespace Kudu.Tests.Core.Deployment.Generator
{
    public class OryxBuilderTests
    {
        private static IEnvironment mockEnvironment = null;
        private static IDeploymentSettingsManager mockDeploymentSettingsManager = null;
        private static IBuildPropertyProvider mockBuildPropertyProvider = null;
        private static string mockSourcePath = null;

        public OryxBuilderTests()
        {
            mockEnvironment = new Kudu.Core.Environment(
                rootPath: "rootPath",
                binPath: "binPath",
                repositoryPath: "repositoryPath",
                requestId: "requestId",
                kuduConsoleFullPath: "kuduConsoleFullPath",
                httpContextAccessor: new HttpContextAccessor());

            mockDeploymentSettingsManager = new DeploymentSettingsManager(XmlSettings.Settings);
        }
    }
}
