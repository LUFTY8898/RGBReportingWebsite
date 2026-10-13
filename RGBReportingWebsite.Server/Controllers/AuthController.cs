using Microsoft.AspNetCore.Mvc;
using RGBReportingWebsite.Server.Models;
using RGBReportingWebsite.Server.Services;

namespace RGBReportingWebsite.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ReportDetailsDatabaseService _reportDetailsService;

        public AuthController(ReportDetailsDatabaseService reportDetailsService)
        {
            _reportDetailsService = reportDetailsService;
        }

        // GET api/Auth/UserContext?login={emailOrLogin}
        [HttpGet("UserContext")]
        public async Task<IActionResult> GetUserContext([FromQuery] string? login)
        {
            string? lookup = login;

            // Try to get email from authenticated user claims if login not supplied
            if (string.IsNullOrWhiteSpace(lookup) && User?.Identity?.IsAuthenticated == true)
            {
                // Common claim types: preferred_username, email
                lookup = User.Claims.FirstOrDefault(c => c.Type == "preferred_username" || c.Type == "email")?.Value;
            }

            if (string.IsNullOrWhiteSpace(lookup))
                return BadRequest("login query parameter or authenticated user email required.");

            var ctx = await _reportDetailsService.GetUserContextByLoginAsync(lookup);

            if (ctx == null)
                return NotFound();

            return Ok(ctx);
        }
    }
}
