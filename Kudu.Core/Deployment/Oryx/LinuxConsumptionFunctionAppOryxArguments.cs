using System;
using System.Collections.Generic;
using System.Text;

namespace Kudu.Core.Deployment.Oryx
{
    public class LinuxConsumptionFunctionAppOryxArguments : FunctionAppOryxArguments
    {
        public LinuxConsumptionFunctionAppOryxArguments(IEnvironment env) : base(env)
        {
            SkipKuduSync = true;
            Flags = BuildOptimizationsFlags.Off;
        }

        public override string GenerateOryxBuildCommand(DeploymentContext context, IEnvironment environment)
        {
            StringBuilder args = new StringBuilder();

            base.AddOryxBuildCommand(args, context, source: context.RepositoryPath, destination: context.OutputPath);
            base.AddLanguage(args, base.FunctionsWorkerRuntime);
            base.AddLanguageVersion(args, base.FunctionsWorkerRuntime);
            base.AddBuildOptimizationFlags(args, context, Flags);
            base.AddWorkerRuntimeArgs(args, base.FunctionsWorkerRuntime);

            return args.ToString();
        }
    }
}
