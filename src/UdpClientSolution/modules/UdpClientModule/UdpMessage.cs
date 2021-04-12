namespace UdpClientModule
{
    using System;

    public class UdpMessage
    {
        public DateTime timeStamp {get; set;}
        public string address {get; set;}
        public int port {get; set;}
        public string message {get; set;}
    }

}
