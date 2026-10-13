using Microsoft.AspNetCore.Mvc;
using RGBReportingWebsite.Server.Models;
using RGBReportingWebsite.Server.Services;

namespace RGBReportingWebsite.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportDetailsController : ControllerBase
    {
        private readonly ReportDetailsDatabaseService _reportDetailsService;
        private readonly LoggedInUserAccessor _loggedInUserAccessor;

        public ReportDetailsController(
            ReportDetailsDatabaseService reportDetailsService,
            LoggedInUserAccessor loggedInUserAccessor)
        {
            _reportDetailsService = reportDetailsService;
            _loggedInUserAccessor = loggedInUserAccessor;
        }


        [HttpGet]
        public async Task<ActionResult<List<ReportDetail>>> Get(
     [FromQuery] string userID)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(userID))
                {
                    return BadRequest(
                        "A userID must be supplied.");
                }

                // Resolve user context and populate the scoped LoggedInUserAccessor
                var ctx = await _reportDetailsService.GetUserContextByLoginAsync(userID);
                if (ctx != null)
                {
                    _loggedInUserAccessor.LoggedInUserID = ctx.userID;
                    _loggedInUserAccessor.Login = userID;
                    _loggedInUserAccessor.FullName = ctx.fullName;
                }

                List<ReportDetail> reports =
                    await _reportDetailsService.GetReportDetailsAsync(userID);

                return Ok(reports);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }

        // Resolve a user context (numeric id and SAP ids) by login/email.
        [HttpGet("userContext")]
        public async Task<ActionResult<UserContext>> GetUserContext([FromQuery] string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return BadRequest("A login must be supplied.");

            var ctx = await _reportDetailsService.GetUserContextByLoginAsync(login);
            if (ctx == null)
                return NotFound();

            return Ok(ctx);
        }

        // GET api/ReportDetails/accountManagers?reportDetailID=123&login=user@example.com
        [HttpGet("accountManagers")]
        public async Task<ActionResult<List<AccountManagerOptionDto>>> GetAccountManagers(
            [FromQuery] int reportDetailID,
            [FromQuery] string login)
        {
            try
            {
                if (reportDetailID <= 0 || string.IsNullOrWhiteSpace(login))
                    return BadRequest("A reportDetailID and login must be supplied.");

                var userContext = await _reportDetailsService.GetUserContextByLoginAsync(login);
                if (userContext?.userID == null)
                    return NotFound("The logged-in user could not be resolved.");

                var list = await _reportDetailsService.GetAccountManagersForReportAsync(
                    reportDetailID,
                    userContext.userID.Value);
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }
    }
}
