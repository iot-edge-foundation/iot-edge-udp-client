namespace UdpClientModule
{
    using System.Net;
    using System.Net.Sockets;

    public struct UdpState
    {
        public UdpClient u;
        public IPEndPoint e;
    }

}
