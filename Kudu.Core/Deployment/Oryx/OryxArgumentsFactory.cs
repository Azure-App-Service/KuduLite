using Kudu.Core.Infrastructure;

namespace Kudu.Core.Deployment.Oryx
{
    class OryxArgumentsFactory
    {
        public static IOryxArguments CreateOryxArguments()
        {
            if (FunctionAppHelper.LooksLikeFunctionApp())
            {
                return new FunctionAppOryxArguments();
            }
            return new AppServiceOryxArguments();
        }
    }
}
