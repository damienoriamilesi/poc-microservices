using System;
using System.Threading.Tasks;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;

namespace DotNetMicroservice.Events
{
    public class CounterIncrementEventHandler : IIntegrationEventHandler<CounterIncrementEvent>
    {
        public Task Handle(CounterIncrementEvent @event)
        {
            // TODO > Increment value and store
            throw new NotImplementedException();
        }
    }
}
