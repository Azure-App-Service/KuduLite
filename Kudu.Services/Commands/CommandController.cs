using System;
using System.Collections.Generic;
using Kudu.Contracts.Tracing;
using Kudu.Core;
using Kudu.Core.Commands;
using Kudu.Core.Infrastructure;
using Kudu.Core.Tracing;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;

namespace Kudu.Services.Commands
{
    public class CommandController : Controller
    {
        private readonly ICommandExecutor _commandExecutor;
        private readonly ITracer _tracer;

        public CommandController(ICommandExecutor commandExecutor, ITracer tracer)
        {
            _commandExecutor = commandExecutor;
            _tracer = tracer;
        }

        /// <summary>
        /// Executes an arbitrary command line and return its output
        /// </summary>
        /// <param name="input">The command line to execute</param>
        /// <returns></returns>
        [HttpPost]
        public IActionResult ExecuteCommand([FromBody] JObject input)
        {
            if (input == null)
            {
                return BadRequest();
            }

            string command = input.Value<string>("command");
            string workingDirectory = input.Value<string>("dir");
            using (_tracer.Step("Executing " + command, new Dictionary<string, string> { { "CWD", workingDirectory } }))
            {
                try
                {
                    CommandResult result = _commandExecutor.ExecuteCommand(command, workingDirectory);
                    return Ok(result);
                }
                catch (CommandLineException ex)
                {
                    _tracer.TraceError(ex);
                    return Ok(new CommandResult { Error = ex.Error, ExitCode = ex.ExitCode });
                }
                catch (Exception ex)
                {
                    _tracer.TraceError(ex);
                    return Ok(new CommandResult { Error = ex.ToString(), ExitCode = -1 });
                }
            }
        }
    }
}
