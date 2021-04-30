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
            Console.WriteLine("Hello World UDP Server, listening on 11000!");

            UdpClient udpClient = new UdpClient(11000);
            try
            {
                var i = 0;

                while (true)
                {
                    var text = $"Hello {i} From UDP Server {DateTime.Now.Second}";

                    var lengthOfMessage = text.Length;

                    // Sends a message to the host to which you have connected.
                    Byte[] sendBytes = Encoding.ASCII.GetBytes(text);

                    udpClient.Send(sendBytes,
                                    sendBytes.Length,
                                    "127.0.0.1", //"192.168.1.91"
                                    11001);   // sending a message to a specific client (flexible)

                    i++;

                    Console.WriteLine($"Sent: {text}");

                    Thread.Sleep(10); // was tested with 10ms -> 50+ messages per second
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