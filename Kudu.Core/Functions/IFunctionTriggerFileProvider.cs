namespace Kudu.Core.Functions
{
    public interface IFunctionTriggerFileProvider
    {
        /// <summary>
        /// Retuns the function triggers josn string from function.json files
        /// </summary>
        /// <param name="filePath">The zip file path of the functions</param>
        /// <returns>Json string of the function triggers</returns>
        string GetFunctionTriggers(string filePath);
    }
}
