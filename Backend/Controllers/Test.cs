using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class Test : ControllerBase
    {
        
        [HttpGet]
        public IActionResult test()
        {
            return Ok(new {name="Roudy"});
        }
    }
}
