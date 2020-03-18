using Kudu.Core.Infrastructure;

namespace Kudu.Core.Deployment.Oryx
{
    public class OryxArgumentsFactory
    {
        public static IOryxArguments CreateOryxArguments(IEnvironment environment)
        {
            if (FunctionAppHelper.LooksLikeFunctionApp())
            {
                if (FunctionAppHelper.HasScmRunFromPackage())
                {
                    return new LinuxConsumptionFunctionAppOryxArguments(environment);
                } else {
                    return new FunctionAppOryxArguments(environment);
                }
            }
            return new AppServiceOryxArguments(environment);
        }
    }
}
