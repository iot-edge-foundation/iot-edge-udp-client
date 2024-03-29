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
        private static ModuleClient ioTHubModuleClient;
        private static string _moduleId; 
        private static string _deviceId;
        private static LogLevelMessage.LogLevel DefaultMinimalLogLevel = LogLevelMessage.LogLevel.Warning;
        private const int DefaultClientListeningPort = 11001;
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

            System.Console.WriteLine("");
            System.Console.WriteLine("  _       _                  _                             _                 _ _            _   ");
            System.Console.WriteLine(" (_)     | |                | |                           | |               | (_)          | |  ");
            System.Console.WriteLine("  _  ___ | |_ ______ ___  __| | __ _  ___ ______ _   _  __| |_ __ ______ ___| |_  ___ _ __ | |_ ");
            System.Console.WriteLine(" | |/ _ \\| __|______/ _ \\/ _` |/ _` |/ _ \\______| | | |/ _` | '_ \\______/ __| | |/ _ \\ '_ \\| __|");
            System.Console.WriteLine(" | | (_) | |_      |  __/ (_| | (_| |  __/      | |_| | (_| | |_) |    | (__| | |  __/ | | | |_ ");
            System.Console.WriteLine(" |_|\\___/ \\__|      \\___|\\__,_|\\__, |\\___|       \\__,_|\\__,_| .__/      \\___|_|_|\\___|_| |_|\\__|");
            System.Console.WriteLine("                                __/ |                       | |                                 ");
            System.Console.WriteLine("                               |___/                        |_|                                 ");
            System.Console.WriteLine("Copyrights 2021 @svelde");
            System.Console.WriteLine("");

            MqttTransportSettings mqttSetting = new MqttTransportSettings(TransportType.Mqtt_Tcp_Only);
            ITransportSettings[] settings = { mqttSetting };

            // Open a connection to the Edge runtime
            ioTHubModuleClient = await ModuleClient.CreateFromEnvironmentAsync(settings);
            
            // Execute callback method for Twin desired properties updates
            var twin = await ioTHubModuleClient.GetTwinAsync();
            await onDesiredPropertiesUpdate(twin.Properties.Desired, ioTHubModuleClient);

            Console.WriteLine($"Supported desired properties: minimalLogLevel ({MinimalLogLevel}), clientListeningPort ({ClientListeningPort})."); 

            Console.WriteLine("Attached routing outputs: output1, outputError."); 

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
                    await client.UpdateReportedPropertiesAsync(reportedProperties);

                    Console.WriteLine("Warning: Changes to desired properties will be efficive on restarting the module.");
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

        ///
        /// Decouple user logic from IoT Edge logic (like desired properties or direct methods)
        ///
        private static void ThreadBody()
        {
            // https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.beginreceive?view=net-5.0

            Console.WriteLine($"UDP Client listening on {ClientListeningPort}");

            // IPEndPoint object will allow us to read datagrams sent from any source.
            // The server knows us, we accept all servers
            IPEndPoint RemoteIpEndPoint = new IPEndPoint(IPAddress.Any, ClientListeningPort);
            var udpClient = new UdpClient(RemoteIpEndPoint);

            var udpState = new UdpState();
            udpState.e = RemoteIpEndPoint;
            udpState.u = udpClient;

            Console.WriteLine("listening for first message");

            try
            {
                while (true)
                {
                    // Here we look for an UDP message, over and over again.
                    // we have to repeat this non blocking call
                    udpClient.BeginReceive(new AsyncCallback(ReceiveCallback), udpState); // We use the async pattern.
                    Thread.Sleep(2000);  // TODO Interval ; must be shorter than on server interval!
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
            string receiveString = Encoding.ASCII.GetString(receiveBytes); // Here we expect to receive ASCII STRINGS

            Console.WriteLine($"Received message: {receiveString} coming from server '{e.Address}:{e.Port}' ");

            var udpMessage = new UdpMessage
            {
                timeStamp = DateTime.UtcNow,
                address =  e.Address.ToString(), // server sending the message
                port = e.Port, // server sending the message
                message = receiveString,
            };

            // Pro tip: use a queue to disconnect receiving incoming messages from handling the messages. UDP has no acknowledge, you do not want to miss any message!

            var jsonMessage = JsonConvert.SerializeObject(udpMessage);

            var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

            using (var message = new Message(messageBytes))
            {
                message.ContentEncoding = "utf-8";
                message.ContentType = "application/json";

                message.Properties.Add("ContentEncodingX", "Udp+utf-8+application/json");

                ioTHubModuleClient.SendEventAsync("output1", message).Wait();

                Console.WriteLine($"Message sent.");
            }
        }

        private static async Task SendLogLevelMessage(LogLevelMessage moduleStateMessage)
        {
            if (moduleStateMessage.logLevel < MinimalLogLevel)
            {
                Console.WriteLine($"Error message {moduleStateMessage.code}- Level {moduleStateMessage.logLevel} ignored.");
                
                return;
            }

            var jsonMessage = JsonConvert.SerializeObject(moduleStateMessage);

            var messageBytes = Encoding.UTF8.GetBytes(jsonMessage);

            using (var message = new Message(messageBytes))
            {
                message.ContentEncoding = "utf-8";
                message.ContentType = "application/json";

                message.Properties.Add("ContentEncodingX", "Error+utf-8+application/json");

                await ioTHubModuleClient.SendEventAsync("outputError", message);

                Console.WriteLine($"Error message {moduleStateMessage.code} sent.");
            }
        }
    }
}
