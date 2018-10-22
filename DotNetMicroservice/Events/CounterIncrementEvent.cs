using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Events;

namespace DotNetMicroservice.Events
{
    public class CounterIncrementEvent : IntegrationEvent
    {
        public int Counter { get; set; }
    }

    //public abstract class EventBase
    //{
    //    public Guid Id { get; } = new Guid();
    //    public DateTime CreationDate { get; } = DateTime.UtcNow;
    //}
}