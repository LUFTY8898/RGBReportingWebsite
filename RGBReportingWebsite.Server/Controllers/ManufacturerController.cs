using Microsoft.AspNetCore.Mvc;
using RGBReportingWebsite.Server.Services;

namespace RGBReportingWebsite.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ManufacturerController
        : ControllerBase
    {
        private readonly ManufacturerService
            _manufacturerService;


        public ManufacturerController(
            ManufacturerService manufacturerService)
        {
            _manufacturerService =
                manufacturerService;
        }


        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var manufacturers =
                await _manufacturerService
                    .GetManufacturersAsync();


            return Ok(manufacturers);
        }
    }
}