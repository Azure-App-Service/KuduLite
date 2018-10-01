using System;
using System.Collections.Generic;
using System.Diagnostics;
using Kudu.Contracts.Tracing;
using Kudu.Core.Infrastructure;

namespace Kudu.Core.Tracing
{
    public class ConsoleTracer : ITracer
    {
        public TraceLevel TraceLevel { get; }
        
        public IDisposable Step(string message, IDictionary<string, string> attributes)
        {
            Console.WriteLine("Step : "+message);
            foreach( var k in attributes.Keys)
            {
                Console.Write("<k:"+k+", v:"+attributes[k]+">    ");
            }
            
            return DisposableAction.Noop;
        }

        public void Trace(string message, IDictionary<string, string> attributes)
        {
            Console.WriteLine("Trace Message : "+message);
            Console.WriteLine("Attributes : ");
            foreach( var k in attributes.Keys)
            {
                Console.Write("<k:"+k+", v:"+attributes[k]+">    ");
            }
            
        }
    }
}