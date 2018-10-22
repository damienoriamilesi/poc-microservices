using System;
using System.Net.Sockets;
using Polly;
using Microsoft.Extensions.Logging;


namespace EventBusVertx
{
    public class VertxPersisterConnection : IVertxPersisterConnection
    {
        //private readonly IConnectionFactory _connectionFactory;
        private readonly dynamic _connectionFactory;
        private readonly ILogger<VertxPersisterConnection> _logger;
        private readonly int _retryCount;
        dynamic _connection;
        bool _disposed;

        readonly object _syncRoot = new object();


        public VertxPersisterConnection(dynamic connectionFactory, ILogger<VertxPersisterConnection> logger, int retryCount = 5)
        {
            //_connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _retryCount = retryCount;
        }

        public bool IsConnected { get; set; }
        public bool TryConnect()
        {
            _logger.LogInformation("Vertx Client is trying to connect");

            lock (_syncRoot)
            {
                var policy = Policy.Handle<SocketException>()
                                        //.Or<BrokerUnreachableException>()
                                        .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
                                            {
                                                _logger.LogWarning(ex.ToString());
                                            }
                                        );

                policy.Execute(() =>
                {
                    var vertxBus = new Eventbus();
                    vertxBus.TryConnect();
                });

                if (IsConnected)
                {
                    //_connection.ConnectionBlocked += OnConnectionBlocked;

                    _logger.LogInformation($"RabbitMQ persistent connection acquired a connection {_connection.Endpoint.HostName} and is subscribed to failure events");

                    return true;
                }

                _logger.LogCritical("FATAL ERROR: RabbitMQ connections could not be created and opened");

                return false;
            }
        }

        public Socket CreateSocket()
        {
            var vertxBus = new Eventbus();
            var socket = vertxBus.TryConnect();
            return socket;
        }

        private void OnConnectionShutdown(object sender, EventArgs e)
        {
            if (_disposed) return;

            _logger.LogWarning("A vertx connection is shutdown. Trying to re-connect...");

            TryConnect();
        }
        public void Dispose()
        {
            throw new NotImplementedException();
        }
    }
}
