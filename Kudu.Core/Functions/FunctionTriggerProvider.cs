namespace Kudu.Core.Functions
{
    public class FunctionTriggerProvider
    {
        /// <summary>
        /// Returns the function triggers from function.json files
        /// </summary>
        /// <param name="providerName">The the returns json string differs as per the format expected based upon providers.
        /// e.g. for "KEDA" the json string is the serialzed string of IEnumerable<ScaleTrigger>object</param>
        /// <param name="functionzipFilePath">The functions file path</param>
        /// <returns>The josn string of triggers in function.json</returns>
        public static string GetFunctionTriggers(string providerName, string functionzipFilePath)
        {
            IFunctionTriggerFileProvider functionTriggerProvider;
            if (string.IsNullOrWhiteSpace(providerName))
            {
                return null;
            }

            switch (providerName.ToLower())
            {
                case "keda":
                    functionTriggerProvider = new KedaFunctionTriggerProvider();
                    break;
                default:
                    functionTriggerProvider = new KedaFunctionTriggerProvider();
                    break;
            }

            return functionTriggerProvider.GetFunctionTriggers(functionzipFilePath);
        }
    }
}
