using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;

namespace Kudu.Core.Tracing
{
    public class Log4NetTracer : ITracer
    {
        
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        public TraceLevel TraceLevel { get; }
        
        public IDisposable Step(string message, IDictionary<string, string> attributes)
        {
            return new DisposableAction(() => log.Info(message));
        }

        public void Trace(string message, IDictionary<string, string> attributes)
        {
            log.Debug(message);
        }
    }
}