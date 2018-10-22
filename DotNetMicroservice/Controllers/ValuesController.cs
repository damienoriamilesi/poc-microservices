using System.Collections.Generic;
using DotNetMicroservice.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.eShopOnContainers.BuildingBlocks.EventBus.Abstractions;

namespace DotNetMicroservice.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {
        //private readonly IBasketRepository _repository;
        //private readonly IIdentityService _identitySvc;
        private readonly IEventBus _eventBus;

        public ValuesController(
            //IBasketRepository repository,
            //IIdentityService identityService,
            IEventBus eventBus)
        {
            //_repository = repository;
            //_identitySvc = identityService;
            _eventBus = eventBus;
        }

        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            return new [] { "value1", "value2" };
        }

        // GET api/values/5
        [HttpGet("{id}")]
        public ActionResult<string> Get(int id)
        {
            return "value";
        }

        // POST api/values
        [HttpPost]
        public void Post([FromBody] string value)
        {
            int actualCounter = 50;
            actualCounter++;
            _eventBus.Publish(new CounterIncrementEvent{Counter = actualCounter});
        }

        // PUT api/values/5
        [HttpPut("{id}")]
        public void Put(int id, [FromBody] string value)
        {
        }

        // DELETE api/values/5
        [HttpDelete("{id}")]
        public void Delete(int id)
        {
        }
    }
}
