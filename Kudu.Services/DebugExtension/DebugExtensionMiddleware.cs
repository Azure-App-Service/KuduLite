using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Threading;
using System.Net;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Kudu.Services.TunnelServer
{
    public class DebugExtensionMiddleware 
    {
        
        private const int MAX_BUFFER_SIZE = 65536;
        private const int CLOSE_TASK_TIMEOUT_IN_MILLISECONDS = 5000;
        private static ILoggerFactory loggerFactory = null;
        private static ILogger _logger = null;

        public DebugExtensionMiddleware(RequestDelegate next)
        {
            ConfigureLogging();
        }
        
        private class LSiteStatusResponse
        {
            public int port { get; }
            public string state { get; }
            public bool canReachPort { get; }
            public string msg { get; }

            public LSiteStatusResponse(string state, int port, bool canReachPort)
            {
                this.port = port;
                this.state = state;
                this.canReachPort = canReachPort;
                this.msg = "";
            }

            public LSiteStatusResponse(string state, int port, bool canReachPort, string msg)
                : this(state, port, canReachPort)
            {
                this.msg = msg;
            }
        }
        
        public void ConfigureLogging()
        {
            if (loggerFactory == null)
            {
                loggerFactory = new LoggerFactory();

                string level = Environment.GetEnvironmentVariable("APPSVC_TUNNEL_VERBOSITY");
                LogLevel logLevel = LogLevel.Information;

                if (!string.IsNullOrWhiteSpace(level))
                {
                    if (level.Equals("info", StringComparison.OrdinalIgnoreCase))
                    {
                        logLevel = LogLevel.Information;
                    }
                    if (level.Equals("error", StringComparison.OrdinalIgnoreCase))
                    {
                        logLevel = LogLevel.Error;
                    }
                    if (level.Equals("debug", StringComparison.OrdinalIgnoreCase))
                    {
                        logLevel = LogLevel.Debug;
                    }
                    if (level.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        logLevel = LogLevel.None;
                    }
                    Console.WriteLine("Setting LogLevel to " + level);
                }

                loggerFactory.AddConsole(logLevel);
            }

            if (_logger == null)
            {
                _logger = loggerFactory.CreateLogger<DebugExtensionMiddleware>();
            }
        }

        public async Task Invoke (HttpContext context)
        {
             
                int tunnelPort = -1;
                if (context.Request.Headers.ContainsKey("AppsvcTunnelPort"))
                {
                    tunnelPort = int.Parse(context.Request.Headers["AppsvcTunnelPort"].First());
                }

                int bufferSize = 65536;
                if (context.Request.Headers.ContainsKey("AppsvcTunnelBuffer"))
                {
                    bufferSize = int.Parse(context.Request.Headers["AppsvcTunnelBuffer"].First());
                }

                _logger.LogInformation("Appsvc: " + tunnelPort + " " + bufferSize);

                string ipAddress = null;
                try
                {
                    ipAddress = Environment.GetEnvironmentVariable("APPSVC_TUNNEL_IP");
                    _logger.LogInformation("HandleWebSocketConnection: Found IP Address from APPSVC_TUNNEL_IP: " + ipAddress);
                }
                catch (Exception)
                {
                }

                bool continueIpCheck = true;
                int debugPort = 2222;

                try
                {
                    if (ipAddress == null)
                    {
                        ipAddress = System.IO.File.ReadAllText("/appsvctmp/ipaddr_" + Environment.GetEnvironmentVariable("WEBSITE_ROLE_INSTANCE_ID"));
                        if(ipAddress != null && ipAddress.Contains(':'))
                        {
                            string[] ipAddrPortStr = ipAddress.Split(":");
                            ipAddress = ipAddrPortStr[0];
                            debugPort = Int32.Parse(ipAddrPortStr[1]);
                            continueIpCheck = false;
                            _logger.LogInformation("HandleWebSocketConnection: VNET Conatiner PORT : " + tunnelPort);
                    }
                        _logger.LogInformation("HandleWebSocketConnection: Found IP Address from appsvctmp file: " + ipAddress);
                    }
                }
                catch (Exception)
                {
                }

                try
                {
                    if (continueIpCheck && ipAddress == null)
                    {
                        ipAddress = System.IO.File.ReadAllText("/home/site/ipaddr_" + Environment.GetEnvironmentVariable("WEBSITE_ROLE_INSTANCE_ID"));
                        _logger.LogInformation("HandleWebSocketConnection: Found IP Address from share file: " + ipAddress);
                    }
                }
                catch (Exception)
                {
                }

                if (ipAddress == null)
                {
                    ipAddress = "127.0.0.1";
                }

                _logger.LogInformation("HandleWebSocketConnection: Final IP Address: " + ipAddress);

                if (continueIpCheck)
                {
                    try
                    {
                        debugPort = Int32.Parse(Environment.GetEnvironmentVariable("APPSVC_TUNNEL_PORT"));

                       if (debugPort <= 0)
                        {
                            throw new Exception("Debuggee not found. Please start your site in debug mode and then attach debugger.");
                        }
                    }
                    catch (Exception)
                    {
                        debugPort = 2222;
                    }
                }

                string remoteDebug = "FALSE";
                int remoteDebugPort = -1;

                try
                {
                    remoteDebug = System.IO.File.ReadAllText("/appsvctmp/remotedebug_" + Environment.GetEnvironmentVariable("WEBSITE_ROLE_INSTANCE_ID"));
                    _logger.LogInformation("HandleWebSocketConnection: Found remote debug file: " + remoteDebug);

                    if (!string.IsNullOrWhiteSpace(remoteDebug) && !remoteDebug.Contains("FALSE"))
                    {
                        // remote debug is enabled
                        if (int.TryParse(remoteDebug, out remoteDebugPort))
                        {
                            debugPort = remoteDebugPort;
                            _logger.LogInformation("HandleWebSocketConnection: Found remote debug port from file: " + debugPort);
                        }
                    }
                }
                catch (Exception)
                {
                }

                if (tunnelPort > 0)
                {
                    // this is coming from client side.. override.
                    debugPort = tunnelPort;
                }

                _logger.LogInformation("HandleWebSocketConnection: Final Port: " + debugPort);

                if (context.WebSockets.IsWebSocketRequest)
                {
                    _logger.LogInformation("Got websocket request");
                    WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                    await HandleWebSocketConnection(webSocket, ipAddress, debugPort, bufferSize);
                }
                else
                {
                    // Case insensitive test+comparison of the query string key
                    if (context.Request.QueryString.HasValue && 
                        context.Request.QueryString.Value.IndexOf("GetStatus", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _logger.LogInformation("Got a status request... connecting to " + ipAddress + ":" + debugPort);

                        // The GetStatusAPI response is a plain text or a json(version 2) depending upon the API version
                        bool IsV2StatusAPIRequest = context.Request.QueryString.HasValue
                                              && context.Request.QueryString.Value.IndexOf("GetStatusAPIVer", StringComparison.OrdinalIgnoreCase) >= 0
                                              && context.Request.Query["GetStatusAPIVer"].ToString() == "2";

                        _logger.LogInformation("GetStatusAPIV2Request ? : " + IsV2StatusAPIRequest);

                        // if the file does not exist, it implies that the container has not started
                        var lSiteStatus = "STOPPED";
                        try
                        {
                            String content = System.IO.File.ReadAllText("/appsvctmp/status.txt").ToLower();
                            _logger.LogInformation("\n\nContent : " + content);
                            if (content.Equals("startedlsite"))
                            {
                                lSiteStatus = "STARTED";
                            }
                            else if (content.Equals("startinglsite"))
                            {
                                lSiteStatus = "STARTING";
                            }
                        }
                        catch (IOException)
                        {
                            // pass, since if the file is not present implies
                            // web app is not started yet
                        }
                        catch (Exception ex)
                        {
                            // This should never happen
                            _logger.LogError("Could not read web app state : " + ex.Message);
                        }

                        _logger.LogInformation("Site Status" + lSiteStatus);

                        try
                        {
                            using (Socket testSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
                            {
                                testSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                                testSocket.Connect(new IPEndPoint(IPAddress.Parse(ipAddress), debugPort));
                                context.Response.StatusCode = 200;
                                _logger.LogInformation("GetStats success " + ipAddress + ":" + debugPort);
                                if (IsV2StatusAPIRequest)
                                {
                                    var response = new LSiteStatusResponse(lSiteStatus, debugPort, true);
                                    var json = JsonConvert.SerializeObject(response);
                                    await context.Response.WriteAsync(json);
                                }
                                else
                                {
                                    await context.Response.WriteAsync("SUCCESS:" + debugPort);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex.ToString());
                            context.Response.StatusCode = 200;
                            if (IsV2StatusAPIRequest)
                            {
                                var response = new LSiteStatusResponse(lSiteStatus, debugPort, false, "Unable to connect to WebApp");
                                var json = JsonConvert.SerializeObject(response);
                                await context.Response.WriteAsync(json);
                            }
                            else
                            {
                                await context.Response.WriteAsync("FAILURE:" + debugPort + ":" + "Unable to connect to WebApp");
                            }
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 400;                    
                    }
                }
        }
        
        /**
        *  DebugSessionState holds the websocket and 
        *  debuggeeSocket that are part of a debug session.
        */
        class DebugSessionState
        {
            public Socket DebuggeeSocket { get; private set; }
            public WebSocket DebuggerWebSocket { get; private set; }
            public byte[] Buffer { get; private set; }

            public DebugSessionState(Socket debuggeeSocket, WebSocket webSocket)
            {
                DebuggeeSocket = debuggeeSocket;
                DebuggerWebSocket = webSocket;
                Buffer = new byte[MAX_BUFFER_SIZE];
            }

            public DebugSessionState(Socket debuggeeSocket, WebSocket webSocket, int bufferSize)
            {
                DebuggeeSocket = debuggeeSocket;
                DebuggerWebSocket = webSocket;
                Buffer = new byte[bufferSize];
            }
        }
        
        /**
         * OnDataReceiveFromDebuggee is the async callback called when
         * we have data from debuggeeSocket. On receiving data from debuggee,
         * we forward it on the webSocket to the debugger and issue the next
         * async read on the debuggeeSocket.
         */
        private async static void OnDataReceiveFromDebuggee(IAsyncResult result)
        {
            _logger.LogDebug("OnDataReceiveFromDebuggee called.");
            var debugSessionState = (DebugSessionState)result.AsyncState;
            try
            {
                _logger.LogDebug("OnDataReceiveFromDebuggee EndReceive called.");
                var bytesRead = debugSessionState.DebuggeeSocket.EndReceive(result);

                _logger.LogDebug("OnDataReceiveFromDebuggee EndReceive returned with BytesRead=" + bytesRead);
                if (bytesRead > 0)
                {
                    //
                    // got data from debuggee, need to write it to websocket.
                    //
                    _logger.LogDebug("OnDataReceiveFromDebuggee DebuggerWebSocketState=" + debugSessionState.DebuggerWebSocket.State);
                    if (debugSessionState.DebuggerWebSocket.State == WebSocketState.Open)
                    {
                        ArraySegment<byte> outputBuffer = new ArraySegment<byte>(debugSessionState.Buffer,
                                                                                 0,
                                                                                 bytesRead);
                        _logger.LogDebug("OnDataReceiveFromDebuggee Send to websocket: " + bytesRead);
                        await debugSessionState.DebuggerWebSocket.SendAsync(outputBuffer,
                                                                            WebSocketMessageType.Binary,
                                                                            true,
                                                                            CancellationToken.None);
                        _logger.LogDebug("OnDataReceiveFromDebuggee Sent to websocket: " + bytesRead);
                    }
                }

                //
                // issue next read from debuggee socket
                //
                _logger.LogDebug("OnDataReceiveFromDebuggee: Initiate receive from DebuggeeSocket: " + debugSessionState.DebuggeeSocket.Connected);
                if (debugSessionState.DebuggeeSocket.Connected)
                {
                    debugSessionState.DebuggeeSocket.BeginReceive(debugSessionState.Buffer,
                                                                  0,
                                                                  debugSessionState.Buffer.Length,
                                                                  SocketFlags.None,
                                                                  OnDataReceiveFromDebuggee,
                                                                  debugSessionState);
                    _logger.LogDebug("OnDataReceiveFromDebuggee: BeginReceive called...");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("OnDataReceiveFromDebuggee Exception" + ex.ToString());
                try
                {
                    Task closeTask = debugSessionState.DebuggerWebSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                                                                   "Error communicating with debuggee.",
                                                                   CancellationToken.None);
                    closeTask.Wait(CLOSE_TASK_TIMEOUT_IN_MILLISECONDS);
                }
                catch (Exception) { /* catch all since if close fails, we need to move on. */ }

                if (debugSessionState.DebuggeeSocket != null)
                {
                    try
                    {
                        debugSessionState.DebuggeeSocket.Close();
                    }
                    catch (Exception) { /* catch all since if close fails, we need to move on. */ }
                }
            }
        }

        private async Task HandleWebSocketConnection(WebSocket webSocket, string ipAddress, int debugPort, int bufferSize)
        {
            Socket debuggeeSocket = null;
            String exceptionMessage = "Connection failure. ";
            try
            {
                //
                // Define a maximum message size this handler can receive (1K in this case) 
                // and allocate a buffer to contain the received message. 
                // This buffer will be reused for each receive operation.
                //

                DebugSessionState debugSessionState = null;

                if (debuggeeSocket == null)
                {
                    debuggeeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    if (!debuggeeSocket.Connected)
                    {
                        debuggeeSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                        debuggeeSocket.Connect(new IPEndPoint(IPAddress.Parse(ipAddress), debugPort));

                        _logger.LogInformation("HandleWebSocketConnection: New Connection to local socket: " + ipAddress + ":" + debugPort);

                        if (bufferSize > 0)
                        {
                            debugSessionState = new DebugSessionState(debuggeeSocket, webSocket, bufferSize);
                            _logger.LogInformation("HandleWebSocketConnection: Buffer Size: " + bufferSize);
                        }
                        else
                        {
                            debugSessionState = new DebugSessionState(debuggeeSocket, webSocket);
                        }

                        if (debuggeeSocket.Connected)
                        {
                            _logger.LogInformation("HandleWebSocketConnection: Connected to site debugger on " + ipAddress + ":" + debugPort);
                        }

                        _logger.LogDebug("HandleWebSocketConnection: DebuggeeSocket BeginReceive initiated...");

                        debugSessionState.DebuggeeSocket.BeginReceive(debugSessionState.Buffer,        // receive buffer
                                                                       0,                               // offset
                                                                       debugSessionState.Buffer.Length, // length of buffer
                                                                       SocketFlags.None,
                                                                       OnDataReceiveFromDebuggee,       // callback
                                                                       debugSessionState);              // state
                    }
                    else
                    {
                        // duplicate handshake.
                        // will just let the jvm/debugger decide what to do.
                        _logger.LogDebug("HandleWebSocketConnection: Duplicate connection .. ignored ...");
                    }
                }

                //
                // While the WebSocket connection remains open we run a simple loop that receives messages.
                // If a handshake message is received, connect to the debug port and forward messages to/from
                // the debuggee.
                //

                while (webSocket.State == WebSocketState.Open)
                {
                    byte[] receiveBuffer = null;

                    if (bufferSize > 0)
                    {
                        receiveBuffer = new byte[bufferSize];
                    }
                    else
                    {
                        receiveBuffer = new byte[MAX_BUFFER_SIZE];
                    }

                    WebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), CancellationToken.None);

                    _logger.LogDebug("HandleWebSocketConnection: Got data from websocket: " + receiveResult.Count);

                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("HandleWebSocketConnection: Websocket closed !!");
                        break;
                    }
                    else
                    {
                        int receivedBytes = receiveResult.Count;
                        if (receivedBytes > 0)
                        {
                            if (debuggeeSocket == null)
                            {
                                debuggeeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                                if (!debuggeeSocket.Connected)
                                {
                                    debuggeeSocket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
                                    debuggeeSocket.Connect(new IPEndPoint(IPAddress.Parse(ipAddress), debugPort));
                                    debugSessionState = new DebugSessionState(debuggeeSocket, webSocket);

                                    _logger.LogInformation("HandleWebSocketConnection: Connected to local socket: " + ipAddress + ":" + debugPort);

                                    debugSessionState.DebuggeeSocket.BeginReceive(debugSessionState.Buffer,        // receive buffer
                                                                                   0,                               // offset
                                                                                   debugSessionState.Buffer.Length, // length of buffer
                                                                                   SocketFlags.None,
                                                                                   OnDataReceiveFromDebuggee,       // callback
                                                                                   debugSessionState);              // state
                                }
                                else
                                {
                                    // duplicate handshake.
                                    // will just let the jvm/debugger decide what to do.
                                }
                            }

                            // if send fails, it will throw and we release all resources below.
                            debuggeeSocket.Send(receiveBuffer, 0, receivedBytes, SocketFlags.None);

                            _logger.LogDebug("HandleWebSocketConnection: Sent data to socket: " + receivedBytes);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                // catch all Exceptions as we dont want the server to crash on exceptions.
                _logger.LogError("HandleWebSocket connection exception: " + e.ToString());
                exceptionMessage += e.Message;
            }
            finally
            {
                try
                {
                    Task closeTask = webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                                                          exceptionMessage,
                                                          CancellationToken.None);
                    closeTask.Wait(CLOSE_TASK_TIMEOUT_IN_MILLISECONDS);
                }
                catch (Exception) { /* catch all since if close fails, we need to move on. */ }
                if (debuggeeSocket != null)
                {
                    try
                    {
                        debuggeeSocket.Close();
                        debuggeeSocket = null;
                    }
                    catch (Exception) { /* catch all since if close fails, we need to move on. */ }
                }
            }
        }
    }
}