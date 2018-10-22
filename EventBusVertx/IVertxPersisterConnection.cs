using System;
using System.Net.Sockets;

namespace EventBusVertx
{
    public interface IVertxPersisterConnection : IDisposable
    {
        bool IsConnected { get; set; }

        bool TryConnect();

        Socket CreateSocket();
    }
}
