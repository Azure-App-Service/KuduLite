namespace Kudu.Services
{
    /// <summary>
    /// Contract to return JSON Exception response
    /// when calls are made to the REST APIs
    /// </summary>
    public class ExceptionResponse
    {
        string exceptionMessage;
        string exceptionStackTrace;

        public ExceptionResponse(string exceptionMessage, string exceptionStackTrace)
        {
            this.exceptionMessage = exceptionMessage;
            this.exceptionStackTrace = exceptionStackTrace;
        }
    }
}