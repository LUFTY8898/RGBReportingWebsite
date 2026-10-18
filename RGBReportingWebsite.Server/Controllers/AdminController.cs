using Microsoft.AspNetCore.Mvc;
using RGBReportingWebsite.Server.Models;
using RGBReportingWebsite.Server.Services;

namespace RGBReportingWebsite.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly ReportDetailsDatabaseService _reportDetailsService;

        public AdminController(ReportDetailsDatabaseService reportDetailsService)
        {
            _reportDetailsService = reportDetailsService;
        }

        [HttpGet("users")]
        public async Task<ActionResult<List<UserDetailsDto>>> GetUsers()
        {
            try
            {
                var list = await _reportDetailsService.GetAllUsersAsync();
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpPut("users/{userID}")]
        public async Task<IActionResult> UpdateUser(int userID, [FromBody] UserDetailsDto dto)
        {
            if (dto == null || dto.userID != userID)
                return BadRequest("Invalid payload");

            var ok = await _reportDetailsService.UpdateUserFlagsAsync(userID, dto.active, dto.runReports, dto.scheduleReports, dto.manageReports, dto.administrator);
            if (!ok)
                return StatusCode(500, "Update failed");

            return NoContent();
        }

        [HttpGet("users/{userID}/reportPermissions")]
        public async Task<ActionResult<List<ReportPermissionDto>>> GetReportPermissions(int userID)
        {
            if (userID <= 0)
                return BadRequest("A valid userID must be supplied.");

            try
            {
                var permissions = await _reportDetailsService.GetReportPermissionsAsync(userID);
                return Ok(permissions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpPut("users/{userID}/reportPermissions/{reportDetailID}")]
        public async Task<IActionResult> UpdateReportPermission(int userID, int reportDetailID, [FromBody] bool enabled)
        {
            if (userID <= 0 || reportDetailID <= 0)
                return BadRequest("Valid userID and reportDetailID values must be supplied.");

            var updated = await _reportDetailsService.SetReportPermissionAsync(userID, reportDetailID, enabled);
            if (!updated)
                return StatusCode(500, "Unable to save report permission.");

            return NoContent();
        }

        [HttpGet("reportSchedules")]
        public async Task<ActionResult<List<ReportScheduleDto>>> GetReportSchedules()
        {
            try
            {
                return Ok(await _reportDetailsService.GetReportSchedulesAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpPost("reportSchedules")]
        public async Task<IActionResult> CreateReportSchedule([FromBody] ReportScheduleDto schedule)
        {
            var scheduledReportID = await _reportDetailsService.SaveReportScheduleAsync(schedule);
            return scheduledReportID is null
                ? StatusCode(500, "Unable to create the scheduled report.")
                : Ok(new { scheduledReportID });
        }

        [HttpPut("reportSchedules/{scheduledReportID:int}")]
        public async Task<IActionResult> UpdateReportSchedule(int scheduledReportID, [FromBody] ReportScheduleDto schedule)
        {
            if (scheduledReportID <= 0 || schedule.scheduledReportID != scheduledReportID)
                return BadRequest("The route and payload scheduledReportID must match.");

            var savedID = await _reportDetailsService.SaveReportScheduleAsync(schedule);
            return savedID is null ? NotFound() : NoContent();
        }

        [HttpDelete("reportSchedules/{scheduledReportID:int}")]
        public async Task<IActionResult> DeleteReportSchedule(int scheduledReportID)
        {
            if (scheduledReportID <= 0)
                return BadRequest("A valid scheduledReportID must be supplied.");

            return await _reportDetailsService.DeleteReportScheduleAsync(scheduledReportID)
                ? NoContent()
                : NotFound();
        }

        [HttpGet("reportSchedules/{scheduledReportID:int}/emails")]
        public async Task<ActionResult<List<ReportScheduleEmailDto>>> GetReportScheduleEmails(int scheduledReportID)
        {
            if (scheduledReportID <= 0)
                return BadRequest("A valid scheduledReportID must be supplied.");

            try
            {
                return Ok(await _reportDetailsService.GetReportScheduleEmailsAsync(scheduledReportID));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpPost("reportSchedules/{scheduledReportID:int}/emails")]
        public async Task<IActionResult> CreateReportScheduleEmail(int scheduledReportID, [FromBody] ReportScheduleEmailDto email)
        {
            if (scheduledReportID <= 0 || email.scheduledReportID != scheduledReportID || email.userID is not > 0)
                return BadRequest("A valid scheduledReportID and userID must be supplied.");
            if (email.type?.Length > 3 || email.frequency?.Length > 20)
                return BadRequest("Type must be at most 3 characters and frequency at most 20 characters.");

            var emailID = await _reportDetailsService.SaveReportScheduleEmailAsync(email);
            return emailID is null
                ? StatusCode(500, "Unable to create the scheduled-report recipient.")
                : Ok(new { reportScheduleEmailID = emailID });
        }

        [HttpPut("reportSchedules/{scheduledReportID:int}/emails/{emailID:int}")]
        public async Task<IActionResult> UpdateReportScheduleEmail(
            int scheduledReportID,
            int emailID,
            [FromBody] ReportScheduleEmailDto email)
        {
            if (scheduledReportID <= 0 || emailID <= 0 ||
                email.scheduledReportID != scheduledReportID ||
                email.reportScheduleEmailID != emailID || email.userID is not > 0)
                return BadRequest("The route and payload schedule/email IDs must match and userID must be valid.");
            if (email.type?.Length > 3 || email.frequency?.Length > 20)
                return BadRequest("Type must be at most 3 characters and frequency at most 20 characters.");

            var savedID = await _reportDetailsService.SaveReportScheduleEmailAsync(email);
            return savedID is null ? NotFound() : NoContent();
        }

        [HttpDelete("reportSchedules/{scheduledReportID:int}/emails/{emailID:int}")]
        public async Task<IActionResult> DeleteReportScheduleEmail(int scheduledReportID, int emailID)
        {
            if (scheduledReportID <= 0 || emailID <= 0)
                return BadRequest("Valid schedule and email IDs must be supplied.");

            return await _reportDetailsService.DeleteReportScheduleEmailAsync(scheduledReportID, emailID)
                ? NoContent()
                : NotFound();
        }

        [HttpGet("reportSchedules/{scheduledReportID:int}/history")]
        public async Task<ActionResult<List<ReportScheduleHistoryDto>>> GetReportScheduleHistory(int scheduledReportID)
        {
            if (scheduledReportID <= 0)
                return BadRequest("A valid scheduledReportID must be supplied.");

            try
            {
                return Ok(await _reportDetailsService.GetReportScheduleHistoryAsync(scheduledReportID));
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }

        [HttpGet("reportFrequencies")]
        public async Task<ActionResult<List<string>>> GetReportFrequencies()
        {
            try
            {
                return Ok(await _reportDetailsService.GetActiveReportFrequenciesAsync());
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
            }
        }
    }
}
