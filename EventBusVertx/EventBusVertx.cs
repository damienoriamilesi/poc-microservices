using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Autofac;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EventBusVertx
{
    public class EventBusVertx : IEventBus, IDisposable
    {
        public static int counter = 0;

        const string BROKER_NAME = "myApp_event_bus";

        private readonly IVertxPersisterConnection _persistentConnection;
        private readonly ILogger<EventBusVertx> _logger;
        private readonly IEventBusSubscriptionsManager _subsManager;
        private readonly ILifetimeScope _autofac;
        private readonly string AUTOFAC_SCOPE_NAME = "myApp_vertx_event_bus";
        private const string INTEGRATION_EVENT_SUFIX = "IntegrationEvent";
        private readonly int _retryCount;

        private Socket _consumerSocket;
        private string _queueName;

        public EventBusVertx(IVertxPersisterConnection persistentConnection, ILogger<EventBusVertx> logger,
            //ILifetimeScope autofac, IEventBusSubscriptionsManager subsManager, 
            string queueName = null, int retryCount = 5)
        {
            _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            //_subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
            _queueName = queueName;
            _consumerSocket = _persistentConnection.CreateSocket();
            //_autofac = autofac;
            _retryCount = retryCount;
            //_subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
        }

        public void Publish(IntegrationEvent @event)
        {
            if (!_persistentConnection.IsConnected)
            {
                _persistentConnection.TryConnect();
            }
            //vertxEventBusBase.Publish("topic",new JObject(), new Headers());
            //_persistentConnection.CreateModel();
        }

        public void Subscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
        {
            //if (!_consumerChannel.Connected)
            //{
            //    _consumerChannel = _persistentConnection.CreateModel();
            //}

            var eb = new Eventbus();
            eb.TryConnect();
            eb.register(
                "pcs.status",
                new Handlers(
                        "pcs.status",
                        new Action<JObject>(
                            message =>
                            {
                                counter += 5;
                                Console.WriteLine(message);
                            }
                        )
                    )
                );
        }

        public void SubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            throw new NotImplementedException();
        }

        public void UnsubscribeDynamic<TH>(string eventName) where TH : IDynamicIntegrationEventHandler
        {
            throw new NotImplementedException();
        }

        public void Unsubscribe<T, TH>() where T : IntegrationEvent where TH : IIntegrationEventHandler<T>
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }
        private async Task ProcessEvent(string eventName, string message)
        {
            if (_subsManager.HasSubscriptionsForEvent(eventName))
            {
                using (var scope = _autofac.BeginLifetimeScope(AUTOFAC_SCOPE_NAME))
                {
                    var subscriptions = _subsManager.GetHandlersForEvent(eventName);
                    foreach (var subscription in subscriptions)
                    {
                        if (subscription.IsDynamic)
                        {
                            var handler = scope.ResolveOptional(subscription.HandlerType) as IDynamicIntegrationEventHandler;
                            dynamic eventData = JObject.Parse(message);
                            await handler.Handle(eventData);
                        }
                        else
                        {
                            var eventType = _subsManager.GetEventTypeByName(eventName);
                            var integrationEvent = JsonConvert.DeserializeObject(message, eventType);
                            var handler = scope.ResolveOptional(subscription.HandlerType);
                            var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
                            await (Task)concreteType.GetMethod("Handle").Invoke(handler, new [] { integrationEvent });
                        }
                    }
                }
            }
        }

    }
}
