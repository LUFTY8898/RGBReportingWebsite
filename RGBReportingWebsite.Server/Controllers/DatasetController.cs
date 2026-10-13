using Microsoft.AspNetCore.Mvc;
using RGBReportingWebsite.Server.Services;
using System.Data;

namespace RGBReportingWebsite.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DatasetController : ControllerBase
    {
        private readonly DatasetService _datasetService;
        private readonly ReportDetailsDatabaseService _reportDetailsService;
        private readonly LoggedInUserAccessor _loggedInUserAccessor;

        public DatasetController(
            DatasetService datasetService,
            ReportDetailsDatabaseService reportDetailsService,
            LoggedInUserAccessor loggedInUserAccessor)
        {
            _datasetService = datasetService;
            _reportDetailsService = reportDetailsService;
            _loggedInUserAccessor = loggedInUserAccessor;
        }


        [HttpGet("{datasetName}/{reportDetailID:int}")]
        public async Task<IActionResult> GetDataset(
        string datasetName,
        int reportDetailID,
        [FromQuery] string? accountManager = null)
        {
            DataTable table =
                await _datasetService.GenerateDatasetAsync(
                    datasetName,
                    reportDetailID,
                    accountManagerValue: accountManager);

            return Ok(
                ConvertTable(table));
        }

        private static List<Dictionary<string, object?>>
            ConvertTable(DataTable table)
        {
            List<Dictionary<string, object?>> rows = new();


            foreach (DataRow row in table.Rows)
            {
                Dictionary<string, object?> item = new();


                foreach (DataColumn column in table.Columns)
                {
                    object value = row[column];

                    item[column.ColumnName] =
                        value == DBNull.Value
                            ? null
                            : value;
                }


                rows.Add(item);
            }


            return rows;
        }

        [HttpGet("{reportDetailID:int}/accountManagers")]
        public async Task<IActionResult> GetAccountManagers(
            int reportDetailID,
            [FromQuery] string? userID)
        {
            if (reportDetailID <= 0)
                return BadRequest("A valid reportDetailID must be supplied.");
            // Prefer middleware-resolved numeric user id
            int? resolvedUserId = _loggedInUserAccessor?.LoggedInUserID;

            // If not available, try parse query parameter as numeric id
            if (!resolvedUserId.HasValue && !string.IsNullOrWhiteSpace(userID))
            {
                if (int.TryParse(userID, out int parsed))
                {
                    resolvedUserId = parsed;
                }
                else
                {
                    // Try resolving by login string via ReportDetailsDatabaseService
                    var ctx = await _reportDetailsService.GetUserContextByLoginAsync(userID);
                    if (ctx?.userID != null)
                        resolvedUserId = ctx.userID;
                }
            }

            if (!resolvedUserId.HasValue)
                return BadRequest("Unable to determine numeric user id for account manager lookup.");

            var list = await _dataset_service_GetAccountManagersWrapper(reportDetailID, resolvedUserId.Value);

            return Ok(list);
        }

        // Wrapper to call DatasetService.GetAccountManagersForDropdownAsync
        private Task<List<Dictionary<string, object?>>> _dataset_service_GetAccountManagersWrapper(int reportDetailID, int loggedInUserID)
        {
            // Use the improved account managers method that returns userID as the option value
            return _datasetService.GetAccountManagersForDropdownAsync(loggedInUserID, reportDetailID);
        }
    }
}