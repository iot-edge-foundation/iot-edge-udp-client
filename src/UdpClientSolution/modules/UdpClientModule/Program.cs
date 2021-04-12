namespace UdpClientModule
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Runtime.Loader;
    using System.Security.Cryptography.X509Certificates;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Devices.Client;
    using Microsoft.Azure.Devices.Client.Transport.Mqtt;
    using Microsoft.Azure.Devices.Shared;
    using Newtonsoft.Json;
    using System.Net;
    using System.Net.Sockets;

    class Program
    {
        private static LogLevelMessage.LogLevel DefaultMinimalLogLevel = LogLevelMessage.LogLevel.Warning;

        private static ModuleClient ioTHubModuleClient;

        private const int DefaultClientListeningPort = 11001;

        private static string _moduleId; 

        private static string _deviceId;

        private static UdpState _udpState;

        private static LogLevelMessage.LogLevel MinimalLogLevel { get; set; } = DefaultMinimalLogLevel;

        private static int ClientListeningPort { get; set; } = DefaultClientListeningPort;


        static void Main(string[] args)
        {
            Init().Wait();

            // Wait until the app unloads or is cancelled
            var cts = new CancellationTokenSource();
            AssemblyLoadContext.Default.Unloading += (ctx) => cts.Cancel();
            Console.CancelKeyPress += (sender, cpe) => cts.Cancel();
            WhenCancelled(cts.Token).Wait();
        }

        /// <summary>
        /// Handles cleanup operations when app is cancelled or unloads
        /// </summary>
        public static Task WhenCancelled(CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        /// <summary>
        /// Initializes the ModuleClient and sets up the callback to receive
        /// messages containing temperature information
        /// </summary>
        static async Task Init()
        {
            _deviceId = System.Environment.GetEnvironmentVariable("IOTEDGE_DEVICEID");
            _moduleId = Environment.GetEnvironmentVariable("IOTEDGE_MODULEID");

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            
            // Execute callback method for Twin desired properties updates
            var twin = await ioTHubModuleClient.GetTwinAsync();
            await onDesiredPropertiesUpdate(twin.Properties.Desired, ioTHubModuleClient);

            Console.WriteLine("Supported desired properties: minimalLogLevel."); 


            await ioTHubModuleClient.OpenAsync();

            Console.WriteLine($"Module '{_deviceId}'-'{_moduleId}' initialized.");

            var thread = new Thread(() => ThreadBody());
            thread.Start();
        }


        private static async Task onDesiredPropertiesUpdate(TwinCollection desiredProperties, object userContext)
        {
            if (desiredProperties.Count == 0)
            {
                Console.WriteLine("Empty desired properties ignored.");

                return;
            }

            try
            {
                Console.WriteLine("Desired property change:");
                Console.WriteLine(JsonConvert.SerializeObject(desiredProperties));

                var client = userContext as ModuleClient;

                if (client == null)
                {
                    throw new InvalidOperationException($"UserContext doesn't contain expected ModuleClient");
                }

                var reportedProperties = new TwinCollection();
                
                if (desiredProperties.Contains("clientListeningPort"))
                {
                    if (desiredProperties["clientListeningPort"] != null)
                    {
                        ClientListeningPort = Convert.ToInt32(desiredProperties["clientListeningPort"]);
                    }
                    else
                    {
                        ClientListeningPort = DefaultClientListeningPort;
                    }

                    Console.WriteLine($"ClientListeningPort changed to {ClientListeningPort}");

                    reportedProperties["clientListeningPort"] = ClientListeningPort;
                }

                if (desiredProperties.Contains("minimalLogLevel"))
                {
                    if (desiredProperties["minimalLogLevel"] != null)
                    {
                        var minimalLogLevel = desiredProperties["minimalLogLevel"];

                        // casting from int to enum needed
                        var minimalLogLevelInteger = Convert.ToInt32(minimalLogLevel);

                        MinimalLogLevel = (LogLevelMessage.LogLevel)minimalLogLevelInteger;
                    }
                    else
                    {
                        MinimalLogLevel = DefaultMinimalLogLevel;
                    }

                    Console.WriteLine($"MinimalLogLevel changed to '{MinimalLogLevel}'");

                    reportedProperties["minimalLogLevel"] = MinimalLogLevel;
                }
                else
                {
                    Console.WriteLine($"MinimalLogLevel ignored");
                }

                if (reportedProperties.Count > 0)
                {
                    await client.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);

                    Console.WriteLine("Changes to desired properties will be efficive on restarting the module.");
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine($"Desired properties change error: {ex.Message}");

                var logLevelMessage = new LogLevelMessage { logLevel = LogLevelMessage.LogLevel.Error, code = "98", message = $"Desired properties change error: {ex.Message}" };

                await SendLogLevelMessage(logLevelMessage);

                foreach (Exception exception in ex.InnerExceptions)
                {
                    Console.WriteLine($"Error when receiving desired properties: {exception}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error when receiving desired properties: {ex.Message}");

                var logLevelMessage = new LogLevelMessage { logLevel = LogLevelMessage.LogLevel.Error, code = "99", message = $"Error when receiving desired properties: {ex.Message}" };

                await SendLogLevelMessage(logLevelMessage);
            }
        }

        private static void ThreadBody()
        {
            // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.beginreceive?view=net-5.0

            Console.WriteLine($"UDP Client listening on {ClientListeningPort}");

            // IPEndPoint object will allow us to read datagrams sent from any source.
            // The server knows us, we accept all servers
            IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, ClientListeningPort);
            var udpClient = new UdpClient(RemoteIpEndPoint);

            _udpState = new UdpState();
            _udpState.e = RemoteIpEndPoint;
            _udpState.u = udpClient;

            Console.WriteLine("listening for first message");

            try
            {
                while (true)
                {
                    // we have to repeat this non blocking call
                    udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), _udpState);
                    //Console.WriteLine("non blocking call");
                    Thread.Sleep(2000);
                }
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"Fatal ThreadBody exception: {ex.Message}");

                var logLevelMessage = new LogLevelMessage { logLevel = LogLevelMessage.LogLevel.Critical, code = "00", message = $"ThreadBody exception: {ex.Message}" };

                SendLogLevelMessage(logLevelMessage).Wait();

                Console.WriteLine("Halted...");    
            }
            finally
            {
                udpClient.Close();
                udpClient = null;
            }
        }

        private static void ReceiveCallback(IAsyncResult ar)
        {
            UdpClient u = ((UdpState)(ar.AsyncState)).u;
            IPEndPoint e = ((UdpState)(ar.AsyncState)).e;

            byte[] receiveBytes = u.EndReceive(ar, ref e);
            string receiveString = Encoding.ASCII.GetString(receiveBytes);

            Console.WriteLine($"Received message: {receiveString} coming from server '{e.Address}:{e.Port}' ");

            // simulate heavy logic running side-by-side async
            //Thread.Sleep(10000);

            Console.WriteLine($"Closed {receiveString}");
        }

        private static async Task SendLogLevelMessage(LogLevelMessage moduleStateMessage)
        {
            if (moduleStateMessage.logLevel < MinimalLogLevel)
            {
                return;
            }

            var jsonMessage = JsonConvert.SerializeObject(moduleStateMessage);

            var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

            using (var message = new Message(messageBytes))
            {
                message.ContentEncoding = "utf-8";
                message.ContentType = "application/json";
                message.Properties.Add("content-type", "application/opcua-error-json");

                await ioTHubModuleClient.SendEventAsync("outputError", message);

                var size = CalculateSize(messageBytes);

                Console.WriteLine($"Error message {moduleStateMessage.code} with size {size} bytes sent.");
            }
        }

        private static int CalculateSize(byte[] messageBytes)
        {
            using (var message = new Message(messageBytes))
            {
                message.ContentEncoding = "utf-8";
                message.ContentType = "application/json";
                message.Properties.Add("content-type", "application/opcua-error-json"); // not flexible

                var result = message.GetBytes().Length;

                foreach (var p in message.Properties)
                {
                    result = result + p.Key.Length + p.Value.Length;
                }

                return result;
            }
        }
    }

        public struct UdpState
        {
            public UdpClient u;
            public IPEndPoint e;
        }

}
