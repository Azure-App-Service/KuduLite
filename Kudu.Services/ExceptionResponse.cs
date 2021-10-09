namespace Kudu.Services
{
    internal class ExceptionResponse
    {
        private string _exceptionMessage;
        private string _exceptionStackTrace;

        /// <summary>
        /// Contract to return JSON Exception response
        /// when calls are made to the REST APIs
        /// </summary>
        /// <param name="exceptionMessage"></param>
        /// <param name="exceptionStackTrace"></param>
        public ExceptionResponse(string exceptionMessage, string exceptionStackTrace)
        {
            this._exceptionMessage = exceptionMessage;
            this._exceptionStackTrace = exceptionStackTrace;
        }
    }
}