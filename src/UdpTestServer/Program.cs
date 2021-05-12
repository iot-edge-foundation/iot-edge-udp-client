using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace UdpTestServer
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Hello, I'm a UDP Server and I'm listening on port 11000!");

            UdpClient udpClient = new UdpClient(11000);
            try
            {
                var i = 0;

                while (true)
                {
                    var text = $"Hello {i} From UDP Server at {DateTime.UtcNow}";

                    var lengthOfMessage = text.Length;

                    // Sends a message to the host to which you have connected.
                    Byte[] sendBytes = Encoding.ASCII.GetBytes(text);

                    // Sending a message to a specific client
                    udpClient.Send(sendBytes,
                                    sendBytes.Length,
                                    "127.0.0.1", // client address
                                    11001);      // client port

                    i++;

                    Console.WriteLine($"Sent: {text}");

                    Thread.Sleep(10); // test with 10ms -> 50+ messages per second
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception {ex.Message}");
                Console.WriteLine("Press a key to exit");
                Console.ReadLine();
            }
            finally
            {
                udpClient.Close();
            }
        }
    }
}