using Kudu.Core.Infrastructure;

namespace Kudu.Core.Deployment.Oryx
{
    class OryxArgumentsFactory
    {
        public static IOryxArguments CreateOryxArguments()
        {
            if (FunctionAppHelper.LooksLikeFunctionApp())
            {
                if (FunctionAppHelper.HasScmRunFromPackage())
                {
                    return new LinuxConsumptionFunctionAppOryxArguments();
                } else {
                    return new FunctionAppOryxArguments();
                }
            }
            return new AppServiceOryxArguments();
        }
    }
}
