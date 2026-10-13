using Microsoft.AspNetCore.Mvc;
using RGBReportingWebsite.Server.Models;
using RGBReportingWebsite.Server.Services;
using System.Data;

namespace RGBReportingWebsite.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RunReportController : ControllerBase
    {
        private readonly ReportDetailsDatabaseService _reportDetailsService;
        private readonly DatasetService _datasetService;
        private readonly StandardReportService _standardReportService;


        public RunReportController(
            ReportDetailsDatabaseService reportDetailsService,
            DatasetService datasetService,
            StandardReportService standardReportService)
        {
            _reportDetailsService =
                reportDetailsService;

            _datasetService =
                datasetService;

            _standardReportService =
                standardReportService;
        }

        // ============================================================
        // DIAGNOSTIC: test inserting into reportHistory and return any error
        // ============================================================
        [HttpPost("diagnostic/loghistory")]
        public async Task<IActionResult> DiagnosticLogHistory([FromBody] DiagnosticLogRequest request)
        {
            if (request == null)
                return BadRequest("Request required.");

            int? numericUserID = null;
            if (request.userID != null && int.TryParse(request.userID, out int parsed))
                numericUserID = parsed;

            var result = await _reportDetailsService.TryInsertReportHistoryAsync(
                numericUserID,
                request.loginUsed,
                request.reportID,
                request.DateGenerated == default ? DateTime.UtcNow : request.DateGenerated,
                request.ReportTitle,
                request.ReportFile,
                request.ReportType);

            if (result.Success)
                return Ok(new { success = true });

            return Problem(detail: result.Error ?? "Unknown error", statusCode: StatusCodes.Status500InternalServerError);
        }

        public class DiagnosticLogRequest
        {
            public string? userID { get; set; }
            public string? loginUsed { get; set; }
            public int reportID { get; set; }
            public DateTime DateGenerated { get; set; }
            public string? ReportTitle { get; set; }
            public string? ReportFile { get; set; }
            public string? ReportType { get; set; }
        }


        // ============================================================
        // DOWNLOAD REPORT
        //
        // Example:
        //
        // /api/RunReport/27/download
        //     ?userID=user@email.com
        //     &format=excel
        //     &manufacturerFirmCode=3
        //     &fromDate=2026-10-01
        //     &toDate=2026-10-08
        // ============================================================

        [HttpGet("{reportDetailID:int}/download")]
        public async Task<IActionResult> Download(
            int reportDetailID,
            [FromQuery] string userID,
            [FromQuery] string format = "excel",
            [FromQuery] int? manufacturerFirmCode = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string? manufacturerName = null,
            [FromQuery] string? accountManager = null)
        {
            // --------------------------------------------------------
            // Validate reportDetailID
            // --------------------------------------------------------

            if (reportDetailID <= 0)
            {
                return BadRequest(
                    "A valid reportDetailID must be supplied.");
            }


            // --------------------------------------------------------
            // Validate user
            // --------------------------------------------------------

            if (string.IsNullOrWhiteSpace(userID))
            {
                return BadRequest(
                    "A userID must be supplied.");
            }


            // --------------------------------------------------------
            // Normalise output format
            // --------------------------------------------------------

            format =
                string.IsNullOrWhiteSpace(format)
                    ? "excel"
                    : format.Trim().ToLowerInvariant();


            if (format != "excel" &&
                format != "pdf")
            {
                return BadRequest(
                    "The report format must be either 'excel' or 'pdf'.");
            }


            try
            {
                // ====================================================
                // GET REPORT DETAILS
                // ====================================================

                ReportDetail? report = await _reportDetailsService.GetReportDetailAsync(reportDetailID, userID);

                if (report == null)
                {
                    return NotFound(
                        "The report was not found or you do not " + "have permission to run it.");
                }

                // Resolve numeric user id and loginUsed for logging
                int? numericUserID = null;
                string loginUsed = userID;

                if (!string.IsNullOrWhiteSpace(userID) && int.TryParse(userID, out int parsedUserID))
                {
                    numericUserID = parsedUserID;
                }
                else if (!string.IsNullOrWhiteSpace(userID))
                {
                    try
                    {
                        var foundId = await _reportDetailsService.FindUserIDByLoginAsync(userID);
                        if (foundId.HasValue)
                            numericUserID = foundId.Value;
                    }
                    catch
                    {
                        // ignore lookup failures
                    }
                }

                // Expose lookup outcome for diagnostics (development only)
                try
                {
                    Response.Headers["X-LoginUsed"] = loginUsed ?? string.Empty;
                    Response.Headers["X-UserLookup-Id"] = numericUserID.HasValue ? numericUserID.Value.ToString() : "null";
                }
                catch
                {
                    // ignore header write failures
                }


                // ====================================================
                // REPORT ENABLED
                // ====================================================

                if (report.Enabled != true)
                {
                    return BadRequest(
                        "The selected report is not enabled.");
                }


                // ====================================================
                // QUERY NAME
                // ====================================================

                if (string.IsNullOrWhiteSpace(
                        report.QueryName))
                {
                    return BadRequest(
                        "The selected report does not have a query name.");
                }


                // ====================================================
                // QUERY TYPE
                // ====================================================

                string queryType =
                    report.QueryType?.Trim()
                    ?? string.Empty;


                if (!queryType.Equals(
                        "Query",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(
                        $"Query type '{queryType}' " +
                        "is not currently supported.");
                }


                // ====================================================
                // OUTPUT FORMAT PERMISSION
                // ====================================================

                if (format == "excel" &&
                    report.ExcelGeneration != true)
                {
                    return BadRequest(
                        "Excel generation is not enabled for this report.");
                }


                if (format == "pdf" &&
                    report.PdfGeneration != true)
                {
                    return BadRequest(
                        "PDF generation is not enabled for this report.");
                }


                // ====================================================
                // MANUFACTURER
                //
                // If ManufacturerSelect is blank, this report doesn't
                // support manufacturer filtering.
                //
                // If it contains something such as:
                //
                //     T0.FirmCode
                //
                // DatasetService will use that field.
                // ====================================================

                if (manufacturerFirmCode.HasValue &&
                    string.IsNullOrWhiteSpace(
                        report.manufacturerSelect))
                {
                    return BadRequest(
                        "Manufacturer filtering is not enabled " +
                        "for this report.");
                }


                // ====================================================
                // DATE RANGE
                //
                // DateRangeSelect is now a STRING containing the SQL
                // field to filter.
                //
                // Examples:
                //
                //     T0.DocDate
                //     T0.CreateDate
                //
                // NULL / blank means date filtering is disabled.
                // ====================================================

                bool dateRangeEnabled =
                    !string.IsNullOrWhiteSpace(
                        report.dateRangeSelect);


                if (dateRangeEnabled)
                {
                    if (!fromDate.HasValue)
                    {
                        return BadRequest(
                            "A From Date must be supplied.");
                    }


                    if (!toDate.HasValue)
                    {
                        return BadRequest(
                            "A To Date must be supplied.");
                    }


                    if (toDate.Value.Date <
                        fromDate.Value.Date)
                    {
                        return BadRequest(
                            "The To Date cannot be earlier " +
                            "than the From Date.");
                    }
                }
                else
                {
                    // Ignore dates if the report has no configured
                    // date range field.

                    fromDate = null;

                    toDate = null;
                }


                // ====================================================
                // ACCOUNT MANAGER
                //
                // If AccountManagerSelect is blank, this report doesn't
                // support account manager filtering.
                //
                // If it contains something such as:
                //
                //     T0.SAPsalesID
                //
                // DatasetService will use that field.
                // ====================================================

                List<int> allowedAccountManagers = new();
                string? selectedAccountManagerName = null;

                if (!string.IsNullOrWhiteSpace(report.accountManagerSelect))
                {
                    if (numericUserID is not int resolvedUserID)
                    {
                        return BadRequest(
                            "The logged-in user could not be resolved.");
                    }

                    // Validate account manager selection
                    if (int.TryParse(accountManager, out int parsedSapID))
                    {
                        if (parsedSapID == 0)
                        {
                            // "All Account Managers" option (value 0)
                            // SAPsalesID 0 means "All Account Managers" and should match all records
                            // For security, filter to only those SAPsalesID values that are authorized for the user
                            var accountManagerOptions = await _reportDetailsService.GetAccountManagersForReportAsync(reportDetailID, resolvedUserID);
                            allowedAccountManagers = accountManagerOptions
                                .Select(am => am.value)
                                .Distinct()
                                .ToList();
                            selectedAccountManagerName = "All Account Managers";
                        }
                        else
                        {
                            // Specific account manager selected
                            // Verify the SAPsalesID is among the options for this report
                            var accountManagerOptions = await _reportDetailsService.GetAccountManagersForReportAsync(reportDetailID, resolvedUserID);
                            allowedAccountManagers = accountManagerOptions
                                .Select(am => am.value)
                                .Distinct()
                                .ToList();

                            if (!allowedAccountManagers.Contains(parsedSapID))
                            {
                                return BadRequest(
                                    "The selected account manager is not valid for this report.");
                            }

                            selectedAccountManagerName = accountManagerOptions
                                .FirstOrDefault(am => am.value == parsedSapID)?.text;
                            allowedAccountManagers = new List<int> { parsedSapID };
                        }
                    }
                    else
                    {
                        return BadRequest(
                            "Invalid account manager selection.");
                    }
                }


                // ====================================================
                // GENERATE DATASET
                //
                // QueryName:
                //     determines the dataset.
                //
                // ManufacturerSelect:
                //     SQL field used for manufacturer filtering.
                //
                // DateRangeSelect:
                //     SQL field used for date filtering.
                // ====================================================

                DataTable results =
                    await _datasetService
                        .GenerateDatasetAsync(
                            report.QueryName.Trim(),
                            report.reportDetailID,

                            manufacturerField:
                                report.manufacturerSelect,

                            manufacturerFirmCode:
                                manufacturerFirmCode,

                            dateRangeField:
                                report.dateRangeSelect,

                            fromDate:
                                fromDate,

                            toDate:
                                toDate);


                // ====================================================
                // APPLY ACCOUNT MANAGER FILTER
                // ====================================================

                if (allowedAccountManagers.Count > 0)
                {
                    // Only allow records matching the authorized SAPsalesID values
                    results = FilterDataTableBySAPsalesID(results, allowedAccountManagers, report.accountManagerSelect, accountManager);
                }


                // ====================================================
                // VALIDATE DATASET
                // ====================================================

                if (results.Columns.Count == 0)
                {
                    return Problem(
                        title:
                            "Unable to generate report.",

                        detail:
                            "The generated dataset contains no columns.",

                        statusCode:
                            StatusCodes.Status500InternalServerError);
                }


                // ====================================================
                // PDF
                // ====================================================

                if (format == "pdf")
                {
                    byte[] pdfFile =
                        await _standardReportService
                            .GeneratePdfAsync(
                                report,
                                results);


                    if (pdfFile.Length == 0)
                    {
                        return Problem(
                            title:
                                "Unable to generate PDF report.",

                            detail:
                                "The report generator returned " +
                                "an empty PDF file.",

                            statusCode:
                                StatusCodes.Status500InternalServerError);
                    }


                    string pdfFileName =
                        GetReportFileName(
                            report,
                            ".pdf",
                            manufacturerName,
                            selectedAccountManagerName);

                // reportTitle should be the report title (TitleName) not the filename
                string? reportTitle = report.TitleName ?? report.Description;

                // Attempt to log report history and expose any diagnostic via header
                var logResultPdf = await _reportDetailsService.TryInsertReportHistoryAsync(
                    numericUserID,
                    loginUsed,
                    reportDetailID,
                    DateTime.UtcNow,
                    reportTitle,
                    pdfFileName,
                    "PDF");

                    if (!logResultPdf.Success && !string.IsNullOrWhiteSpace(logResultPdf.Error))
                    {
                        Response.Headers["X-ReportHistory-Error"] = logResultPdf.Error;
                    }
                    else
                    {
                        Response.Headers["X-ReportHistory"] = "Logged";
                    }

                    return File(
                        pdfFile,
                        "application/pdf",
                        pdfFileName);
                }


                // ====================================================
                // EXCEL
                // ====================================================

                byte[] excelFile =
                    await _standardReportService
                        .GenerateAsync(
                            report,
                            results);


                if (excelFile.Length == 0)
                {
                    return Problem(
                        title:
                            "Unable to generate Excel report.",

                        detail:
                            "The report generator returned " +
                            "an empty Excel file.",

                        statusCode:
                            StatusCodes.Status500InternalServerError);
                }


                string excelFileName =
                    GetReportFileName(
                        report,
                        ".xlsx",
                        manufacturerName,
                        selectedAccountManagerName);

                // Attempt to log report history and expose any diagnostic via header
                string? reportTitleX = report.TitleName ?? report.Description;

                var logResultXls = await _reportDetailsService.TryInsertReportHistoryAsync(
                    numericUserID,
                    loginUsed,
                    reportDetailID,
                    DateTime.UtcNow,
                    reportTitleX,
                    excelFileName,
                    "Excel");

                if (!logResultXls.Success && !string.IsNullOrWhiteSpace(logResultXls.Error))
                {
                    Response.Headers["X-ReportHistory-Error"] = logResultXls.Error;
                }
                else
                {
                    Response.Headers["X-ReportHistory"] = "Logged";
                }

                return File(
                    excelFile,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    excelFileName);
            }
            catch (Exception ex)
            {
                return Problem(
                    title:
                        "Unable to generate report.",

                    detail:
                        ex.ToString(),

                    statusCode:
                        StatusCodes.Status500InternalServerError);
            }
        }


        // ============================================================
        // CREATE DOWNLOAD FILE NAME
        // ============================================================

        private static string GetReportFileName(
            ReportDetail report,
            string extension,
            string? dropdownLabel = null,
            string? accountManagerName = null)
        {
            string name;


            // --------------------------------------------------------
            // Prefer SpreadsheetName
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(
                    report.SpreadsheetName))
            {
                name =
                    report.SpreadsheetName.Trim();
            }


            // --------------------------------------------------------
            // Otherwise Description
            // --------------------------------------------------------

            else if (!string.IsNullOrWhiteSpace(
                         report.Description))
            {
                name =
                    report.Description.Trim();
            }


            // --------------------------------------------------------
            // Fallback
            // --------------------------------------------------------

            else
            {
                name =
                    "Report";
            }


            // --------------------------------------------------------
            // Remove existing .xlsx
            // --------------------------------------------------------

            if (name.EndsWith(
                    ".xlsx",
                    StringComparison.OrdinalIgnoreCase))
            {
                name =
                    name.Substring(
                        0,
                        name.Length - 5);
            }


            // --------------------------------------------------------
            // Remove existing .xls
            // --------------------------------------------------------

            if (name.EndsWith(
                    ".xls",
                    StringComparison.OrdinalIgnoreCase))
            {
                name =
                    name.Substring(
                        0,
                        name.Length - 4);
            }


            // --------------------------------------------------------
            // Remove existing .pdf
            // --------------------------------------------------------

            if (name.EndsWith(
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                name =
                    name.Substring(
                        0,
                        name.Length - 4);
            }


            // --------------------------------------------------------
            // Replace invalid filename characters
            // --------------------------------------------------------

            foreach (char character in
                     Path.GetInvalidFileNameChars())
            {
                name =
                    name.Replace(
                        character,
                        '_');
            }

            // Append dropdown label if supplied (for example: " for ManufacturerName")
            if (!string.IsNullOrWhiteSpace(dropdownLabel) &&
                !string.Equals(dropdownLabel, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                string safeLabel = dropdownLabel.Trim();

                foreach (char character in Path.GetInvalidFileNameChars())
                {
                    safeLabel = safeLabel.Replace(character, '_');
                }

                name =
                    name +
                    " for " +
                    safeLabel;
            }

            if (!string.IsNullOrWhiteSpace(accountManagerName))
            {
                string safeManager = accountManagerName.Trim();

                foreach (char character in Path.GetInvalidFileNameChars())
                {
                    safeManager = safeManager.Replace(character, '_');
                }

                name =
                    name + " - " + safeManager;
            }


            if (string.IsNullOrWhiteSpace(name))
            {
                name =
                    "Report";
            }


            return
                name +
                extension;
        }

        // ============================================================
        // FILTER DATATABLE BY SAPsalesID
        // ============================================================

        private static DataTable FilterDataTableBySAPsalesID(DataTable dataTable, List<int> allowedSAPsalesIDs, string? accountManagerSelect, string? accountManager)
        {
            DataTable filtered = dataTable.Clone();

            // If accountManagerSelect is provided, use it to find the corresponding DataColumn
            DataColumn? sapSalesIDColumn = null;
            if (!string.IsNullOrWhiteSpace(accountManagerSelect))
            {
                // Support simple qualified SQL identifiers by trimming whitespace/brackets
                // and taking the final dot-separated identifier (e.g. T0.SlpCode -> SlpCode)
                string cleanedSelector = accountManagerSelect.Trim().Trim('[').Trim(']');
                string[] parts = cleanedSelector.Split('.');

                if (parts.Length > 1)
                {
                    cleanedSelector = parts.Last();
                }

                // Find the DataColumn that matches the cleaned selector
                sapSalesIDColumn = dataTable.Columns
                    .Cast<DataColumn>()
                    .FirstOrDefault(c => c.ColumnName.Equals(cleanedSelector, StringComparison.OrdinalIgnoreCase));
            }

            if (sapSalesIDColumn == null)
            {
                throw new InvalidOperationException(
                    "Unable to find a valid DataColumn for the configured accountManagerSelect: " +
                    accountManagerSelect + " - Available columns: " + string.Join(", ", dataTable.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
            }

            // Convert allowedSAPsalesIDs to a HashSet for faster lookup
            HashSet<int> allowedSet = new HashSet<int>(allowedSAPsalesIDs);

            foreach (DataRow row in dataTable.Rows)
            {
                if (row.IsNull(sapSalesIDColumn))
                {
                    continue;
                }

                if (int.TryParse(
                    Convert.ToString(row[sapSalesIDColumn], System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int sapSalesID) && allowedSet.Contains(sapSalesID))
                {
                    filtered.ImportRow(row);
                }
            }

            return filtered;
        }
    }
}