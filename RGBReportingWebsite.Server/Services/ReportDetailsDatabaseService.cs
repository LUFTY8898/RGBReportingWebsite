using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using RGBReportingWebsite.Server.Models;
using System.Data;
using System.Linq;
using System.Collections.Generic;

namespace RGBReportingWebsite.Server.Services
{
    public class ReportDetailsDatabaseService
    {
        private readonly string _connectionString;
        private readonly ILogger<ReportDetailsDatabaseService> _logger;
        private readonly LoggedInUserAccessor _loggedInUserAccessor;

        // Add this line (adjust the column name if different in your DB)
        private const string numericUserIdCol = "userID";

        public ReportDetailsDatabaseService(
            IConfiguration configuration,
            ILogger<ReportDetailsDatabaseService> logger,
            LoggedInUserAccessor loggedInUserAccessor)
        {
            _logger = logger;
            _loggedInUserAccessor = loggedInUserAccessor;


            // First try config providers for RGB_Reporting
            var cs = configuration.GetConnectionString("RGB_Reporting");

            if (string.IsNullOrWhiteSpace(cs))
            {
                // Fallback: attempt to read Website connection string from connectionConfigs table
                // using an existing connection string named "DefaultConnection".
                var fallback = configuration.GetConnectionString("DefaultConnection");

                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    try
                    {
                        cs = GetWebsiteConnectionStringFromTableAsync(fallback).GetAwaiter().GetResult();
                        if (string.IsNullOrWhiteSpace(cs))
                        {
                            throw new InvalidOperationException("RGB_Reporting connection string has not been configured and fallback lookup returned no value.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to retrieve Website connection string from connectionConfigs table.");
                        throw;
                    }
                }
                else
                {
                    throw new InvalidOperationException("RGB_Reporting connection string has not been configured.");
                }
            }

            _connectionString = cs;
        }

        private async Task<string?> GetWebsiteConnectionStringFromTableAsync(string defaultConnection)
        {
            const string sql = @"
                SELECT TOP(1) [value] FROM dbo.connectionConfigs WHERE [key] = 'Website' OR [name] = 'Website' OR configName = 'Website';";

            try
            {
                await using var connection = new SqlConnection(defaultConnection);
                await connection.OpenAsync();

                await using var command = new SqlCommand(sql, connection);
                var result = await command.ExecuteScalarAsync();
                return result?.ToString();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error querying connectionConfigs table using DefaultConnection.");
                throw;
            }
        }

        // Returns account manager options for a specific report.
        // This queries the reportUsers table for entries associated with the reportDetailID
        // and returns distinct userID / fullName pairs suitable for populating a dropdown.
        public async Task<List<RGBReportingWebsite.Server.Models.AccountManagerOptionDto>> GetAccountManagersForReportAsync(int reportDetailID, int loggedInUserID)
        {
            var results = new List<RGBReportingWebsite.Server.Models.AccountManagerOptionDto>();

            const string sql = @"
                SELECT options.sapSalesID AS value, options.fullName AS text
                FROM
                (
                    SELECT ru.sapSalesID, ru.fullName
                    FROM dbo.reportUsers ru
                    WHERE ru.userID = @loggedInUserID
                      AND ru.sapSalesID IS NOT NULL

                    UNION

                    SELECT authorizedUser.sapSalesID, authorizedUser.fullName
                    FROM dbo.reportAccountManagerAuthorisations ra
                    INNER JOIN dbo.reportUsers authorizedUser
                        ON authorizedUser.sapSalesID = ra.sapUserID
                    WHERE ra.userID = @loggedInUserID
                      AND ra.reportID = @reportDetailID
                      AND ra.sapUserID <> 0
                      AND (authorizedUser.userID IS NULL OR authorizedUser.userID <> @loggedInUserID)
                      AND authorizedUser.sapSalesID IS NOT NULL

                    UNION

                    SELECT allUsers.sapSalesID, allUsers.fullName
                    FROM dbo.reportUsers allUsers
                    WHERE allUsers.active = 1
                      AND allUsers.sapSalesID IS NOT NULL
                      AND (allUsers.userID IS NULL OR allUsers.userID <> @loggedInUserID)
                      AND EXISTS
                      (
                          SELECT 1
                          FROM dbo.reportAccountManagerAuthorisations allAuth
                          WHERE allAuth.userID = @loggedInUserID
                            AND allAuth.reportID = @reportDetailID
                            AND allAuth.sapUserID = 0
                      )
                ) options
                ORDER BY options.fullName;";

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@reportDetailID", reportDetailID);
            command.Parameters.AddWithValue("@loggedInUserID", loggedInUserID);

            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var idObj = reader["value"];
                    var nameObj = reader["text"];

                    if (idObj != DBNull.Value && nameObj != DBNull.Value)
                    {
                        results.Add(new RGBReportingWebsite.Server.Models.AccountManagerOptionDto
                        {
                            value = Convert.ToInt32(idObj),
                            text = nameObj.ToString()
                        });
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to query reportUsers for account managers.");
                throw;
            }
        }

        public async Task<List<ReportDetail>> GetReportDetailsAsync(string userID)
        {
            return await GetReportDetailsAsync(userID, null);
        }

        private async Task<List<ReportDetail>> GetReportDetailsAsync(string userID, int? requestedReportDetailID)
        {
            List<ReportDetail> reports = new();

            const string sql = @"
                SELECT
                    rd.reportDetailID,
                    rd.excelFile,
                    rd.enabled,
                    rd.category,
                    rd.subCategory,
                    rd.description,
                    rd.queryType,
                    rd.queryName,
                    rd.savePath,
                    rd.dynamicFields,
                    rd.defaultFieldType,
                    rd.defaultColumnWidthMinimum,
                    rd.defaultColumnWidthMaximum,
                    rd.spreadsheetName,
                    rd.workbookName,
                    rd.excelGeneration, 
                    rd.pdfGeneration,
                    rd.manufacturerSelect,
                    rd.accountManagerSelect,
                    rd.dateRangeSelect,
                    rd.headingHeight,
                    rd.filePath,
                    rd.title,
                    rd.titleName,
                    rd.titleFontSize,
                    rd.columnTotal,
                    rd.columnTotalType,
                    rd.columnTotalFormat,
                    rd.columnTotalWidth,
                    rd.columnTotalTitle,
                    rd.columnTotalStart,
                    rd.columnTotalEnd,
                    rd.rowTotal,
                    rd.rowTotalType,
                    rd.rowTotalFormat,
                    rd.rowTotalHeight,
                    rd.rowTotalTitle,
                    rd.rowTotalStart,
                    rd.rowTotalEnd,
                    ru.userID AS userID
                FROM dbo.reportDetails rd
                INNER JOIN dbo.reportPermissions rp
                    ON rd.reportDetailID = rp.reportDetailID
                    AND rd.enabled = 1
                INNER JOIN dbo.reportUsers ru
                    ON rp.userID = ru.userID
                WHERE rp.userID = @NumericUserID
                AND ISNULL(rp.enabled, 0) = 1
                AND (@ReportDetailID IS NULL OR rd.reportDetailID = @ReportDetailID)
                    ORDER BY
                    rd.category,
                    rd.subCategory,
                    rd.description;";

            _logger.LogInformation(
     "GetReportDetailsAsync called for user {userID}",
     userID);

            _logger.LogInformation(
                "Connection String = {ConnectionString}",
                _connectionString);

            _logger.LogInformation(
                "Windows Identity = {Identity}",
                Environment.UserDomainName + "\\" + Environment.UserName);

            await using SqlConnection connection =
                new SqlConnection(_connectionString);

            try
            {
                _logger.LogInformation("Opening SQL connection...");

                await connection.OpenAsync();

                _logger.LogInformation(
                    "SQL connection opened successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed opening SQL connection.");

                throw;
            }

            await using SqlCommand command =
                new SqlCommand(sql, connection);

            int? numericID = int.TryParse(userID, out int parsedUserID)
                ? parsedUserID
                : await FindUserIDByLoginAsync(userID);

            // Store resolved numeric id into the scoped accessor so other services/controllers
            // in the same request can reuse it without re-querying.
            try
            {
                _loggedInUserAccessor.LoggedInUserID = numericID;
                _loggedInUserAccessor.Login = userID;
            }
            catch
            {
                // ignore if accessor not available
            }

            if (numericID.HasValue)
            {
                command.Parameters.Add("@NumericUserID", SqlDbType.Int).Value = numericID.Value;
            }
            else
            {
                command.Parameters.Add("@NumericUserID", SqlDbType.Int).Value = DBNull.Value;
            }

            command.Parameters.Add("@ReportDetailID", SqlDbType.Int).Value =
                requestedReportDetailID.HasValue
                    ? requestedReportDetailID.Value
                    : DBNull.Value;

            await using SqlDataReader reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                ReportDetail report = new ReportDetail
                {
                    reportDetailID = GetInt(reader, "reportDetailID"),
                    ExcelFile = GetString(reader, "excelFile")?.Trim() ?? string.Empty,
                    Enabled = GetNullableBool(reader, "enabled"),
                    ExcelGeneration = GetNullableBool(reader, "excelGeneration"),
                    PdfGeneration = GetNullableBool(reader, "pdfGeneration"),
                    Category = GetString(reader, "category")?.Trim(),
                    SubCategory = GetString(reader, "subCategory")?.Trim(),
                    Description = GetString(reader, "description")?.Trim(),
                    QueryType = GetString(reader, "queryType")?.Trim(),
                    QueryName = GetString(reader, "queryName")?.Trim(),
                    SavePath = GetString(reader, "savePath")?.Trim(),
                    DynamicFields = GetNullableBool(reader, "dynamicFields"),
                    DefaultFieldType = GetString(reader, "defaultFieldType")?.Trim(),
                    DefaultColumnWidthMinimum = GetNullableDecimal(reader, "defaultColumnWidthMinimum"),
                    DefaultColumnWidthMaximum = GetNullableDecimal(reader, "defaultColumnWidthMaximum"),
                    SpreadsheetName = GetString(reader, "spreadsheetName")?.Trim(),
                    WorkbookName = GetString(reader, "workbookName")?.Trim(),
                    manufacturerSelect = GetString(reader, "manufacturerSelect"),
                    accountManagerSelect = GetString(reader, "accountManagerSelect"),
                    dateRangeSelect = GetString(reader, "dateRangeSelect"),
                    HeadingHeight = GetNullableInt(reader, "headingHeight"),
                    FilePath = GetString(reader, "filePath")?.Trim(),
                    Title = GetNullableBool(reader, "title"),
                    TitleName = GetString(reader, "titleName")?.Trim()?.Trim(),
                    TitleFontSize = GetNullableInt(reader, "titleFontSize"),
                    ColumnTotal = GetNullableBool(reader, "columnTotal"),
                    ColumnTotalType = GetString(reader, "columnTotalType")?.Trim(),
                    ColumnTotalFormat = GetString(reader, "columnTotalFormat")?.Trim(),
                    ColumnTotalWidth = GetNullableDecimal(reader, "columnTotalWidth"),
                    ColumnTotalTitle = GetString(reader, "columnTotalTitle")?.Trim(),
                    ColumnTotalStart = GetNullableInt(reader, "columnTotalStart"),
                    ColumnTotalEnd = GetNullableInt(reader, "columnTotalEnd"),
                    RowTotal = GetNullableBool(reader, "rowTotal"),
                    RowTotalType = GetString(reader, "rowTotalType")?.Trim(),
                    RowTotalFormat = GetString(reader, "rowTotalFormat")?.Trim(),
                    RowTotalHeight = GetNullableDecimal(reader, "rowTotalHeight"),
                    RowTotalTitle = GetString(reader, "rowTotalTitle")?.Trim(),
                    RowTotalStart = GetNullableInt(reader, "rowTotalStart"),
                    RowTotalEnd = GetNullableInt(reader, "rowTotalEnd"),
                    userID = GetNullableInt(reader, "userID"),
                };

                reports.Add(report);
            }

            return reports;
        }

        // ============================================================
        // LOG REPORT HISTORY
        //
        // Inserts a record into dbo.reportHistory. This is a
        // best-effort operation and will not throw on failure.
        // ============================================================
        public async Task LogReportHistoryAsync(
            int? userIDNumber,
            string? loginUsed,
            int reportID,
            DateTime dateGenerated,
            string? reportTitle,
            string? reportFile,
            string? reportType)
        {
            // If we have neither a numeric user id nor a login/email, don't log
            if (!userIDNumber.HasValue && string.IsNullOrWhiteSpace(loginUsed))
                return;

            const string sql = @"
                INSERT INTO dbo.reportHistory (userID, loginUsed, reportID, dateGenerated, reportTitle, reportFile, reportType)
                VALUES (@userID, @loginUsed, @reportID, @dateGenerated, @reportTitle, @reportFile, @reportType);";

            try
            {
                _logger?.LogInformation("Attempting insert into reportHistory: userID={userID}, login={loginUsed}, reportID={reportID}, title={reportTitle}, file={reportFile}, type={reportType}", userIDNumber, loginUsed, reportID, reportTitle, reportFile, reportType);
                await using SqlConnection connection =
                    new SqlConnection(_connectionString);

                await connection.OpenAsync();

                await using SqlCommand command =
                    new SqlCommand(sql, connection);

                // userIDNumber is now an int in DB. If null, insert DBNull.
                if (userIDNumber.HasValue)
                {
                    command.Parameters.Add("@userID", SqlDbType.Int).Value = userIDNumber.Value;
                }
                else
                {
                    command.Parameters.Add("@userID", SqlDbType.Int).Value = DBNull.Value;
                }

                // loginUsed (email) - nvarchar(100)
                object dbLoginUsed = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(loginUsed))
                {
                    string safeLogin = loginUsed.Length > 100 ? loginUsed.Substring(0, 100) : loginUsed;
                    dbLoginUsed = safeLogin;
                }
                command.Parameters.Add("@LoginUsed", SqlDbType.NVarChar, 100).Value = dbLoginUsed;

                command.Parameters.Add("@ReportID", SqlDbType.Int).Value = reportID;
                command.Parameters.Add("@DateGenerated", SqlDbType.DateTime).Value = dateGenerated;

                // reportTitle nvarchar(100)
                object dbReportTitle = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(reportTitle))
                {
                    string safeTitle = reportTitle.Length > 100 ? reportTitle.Substring(0, 100) : reportTitle;
                    dbReportTitle = safeTitle;
                }

                // reportFile nvarchar(260)
                object dbReportFile = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(reportFile))
                {
                    string safeFile = reportFile.Length > 260 ? reportFile.Substring(0, 260) : reportFile;
                    dbReportFile = safeFile;
                }

                object dbReportType = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(reportType))
                {
                    string safeType = reportType.Length > 5 ? reportType.Substring(0, 5) : reportType;
                    dbReportType = safeType;
                }

                command.Parameters.Add("@ReportTitle", SqlDbType.NVarChar, 100).Value = dbReportTitle;
                command.Parameters.Add("@ReportFile", SqlDbType.NVarChar, 260).Value = dbReportFile;
                command.Parameters.Add("@ReportType", SqlDbType.NVarChar, 5).Value = dbReportType;

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to insert report history for userID {userID}, login {loginUsed}, report {reportID}", userIDNumber, loginUsed, reportID);
                // Intentionally swallow exceptions so report generation is unaffected
            }
        }

        // Diagnostic wrapper that returns success state and any error message
        public async Task<(bool Success, string? Error)> TryInsertReportHistoryAsync(
            int? userIDNumber,
            string? loginUsed,
            int reportID,
            DateTime dateGenerated,
            string? reportTitle,
            string? reportFile,
            string? reportType)
        {
            const string sql = @"
                INSERT INTO dbo.reportHistory (userID, loginUsed, reportID, dateGenerated, reportTitle, reportFile, reportType)
                VALUES (@userID, @loginUsed, @reportID, @dateGenerated, @reportTitle, @reportFile, @reportType);";

            try
            {
                await using SqlConnection connection =
                    new SqlConnection(_connectionString);

                await connection.OpenAsync();

                await using SqlCommand command =
                    new SqlCommand(sql, connection);

                if (userIDNumber.HasValue)
                {
                    command.Parameters.Add("@userID", SqlDbType.Int).Value = userIDNumber.Value;
                }
                else
                {
                    command.Parameters.Add("@userID", SqlDbType.Int).Value = DBNull.Value;
                }

                object dbLoginUsed = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(loginUsed))
                {
                    string safeLogin = loginUsed.Length > 100 ? loginUsed.Substring(0, 100) : loginUsed;
                    dbLoginUsed = safeLogin;
                }
                command.Parameters.Add("@loginUsed", SqlDbType.NVarChar, 100).Value = dbLoginUsed;

                command.Parameters.Add("@reportID", SqlDbType.Int).Value = reportID;
                command.Parameters.Add("@dateGenerated", SqlDbType.DateTime).Value = dateGenerated;

                object dbReportTitle = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(reportTitle))
                {
                    string safeTitle = reportTitle.Length > 100 ? reportTitle.Substring(0, 100) : reportTitle;
                    dbReportTitle = safeTitle;
                }

                object dbReportFile = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(reportFile))
                {
                    string safeFile = reportFile.Length > 260 ? reportFile.Substring(0, 260) : reportFile;
                    dbReportFile = safeFile;
                }

                object dbReportType = DBNull.Value;
                if (!string.IsNullOrWhiteSpace(reportType))
                {
                    string safeType = reportType.Length > 5 ? reportType.Substring(0, 5) : reportType;
                    dbReportType = safeType;
                }

                command.Parameters.Add("@ReportTitle", SqlDbType.NVarChar, 100).Value = dbReportTitle;
                command.Parameters.Add("@ReportFile", SqlDbType.NVarChar, 260).Value = dbReportFile;
                command.Parameters.Add("@ReportType", SqlDbType.NVarChar, 5).Value = dbReportType;

                int rows = await command.ExecuteNonQueryAsync();

                _logger?.LogInformation("Insert into reportHistory affected {Rows} rows", rows);

                return (rows > 0, null);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Diagnostic insert to reportHistory failed for userID {userID}, login {loginUsed}, report {reportID}", userIDNumber, loginUsed, reportID);
                return (false, ex.Message);
            }
        }

        // Try to find numeric userID in dbo.reportUsers by matching the provided Azure login
        // against the loginID column and returning the numeric userID column.
        // Returns null if no numeric id is found or on error.
        public async Task<int?> FindUserIDByLoginAsync(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return null;

            try
            {
                await using SqlConnection connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                string sql = $"SELECT TOP 1 [{numericUserIdCol}] FROM dbo.reportUsers WHERE LOWER(LTRIM(RTRIM(loginID))) = @loginNormalized;";

                await using (SqlCommand findCmd = new SqlCommand(sql, connection))
                {
                    string normalized = login.Trim().ToLowerInvariant();
                    findCmd.Parameters.Add("@loginNormalized", System.Data.SqlDbType.NVarChar, 256).Value = normalized;

                    _logger?.LogInformation("Executing user lookup SQL: {Sql} with normalizedLogin={Login}", sql, normalized);

                    var result = await findCmd.ExecuteScalarAsync();

                    if (result == null || result == DBNull.Value)
                    {
                        _logger?.LogInformation("User lookup returned no rows for login {Login}", normalized);
                        return null;
                    }

                    _logger?.LogInformation("User lookup raw result type={Type} value={Value}", result.GetType().Name, result);

                    // Convert the returned numeric id to int
                    try
                    {
                        return Convert.ToInt32(result);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Unable to convert user lookup result to int for login {Login}", login);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "FindUserIdByLoginAsync failed for login {Login}", login);
                return null;
            }
        }


        private static string? GetString(
            SqlDataReader reader,
            string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetString(ordinal);
        }


        private static int GetInt(
            SqlDataReader reader,
            string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            return reader.GetInt32(ordinal);
        }


        private static int? GetNullableInt(
            SqlDataReader reader,
            string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetInt32(ordinal);
        }


        private static bool? GetNullableBool(
            SqlDataReader reader,
            string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetBoolean(ordinal);
        }


        private static decimal? GetNullableDecimal(
            SqlDataReader reader,
            string columnName)
        {
            int ordinal = reader.GetOrdinal(columnName);

            if (reader.IsDBNull(ordinal))
                return null;

            return reader.GetDecimal(ordinal);
        }

        public async Task<ReportDetail?> GetReportDetailAsync(int reportDetailID, string userID)
        {
            if (reportDetailID <= 0 || string.IsNullOrWhiteSpace(userID))
                return null;

            var reports = await GetReportDetailsAsync(userID, reportDetailID);
            return reports.FirstOrDefault();
        }

        // Get SAP ids and user info by login (azure email or loginID)
        public async Task<UserContext?> GetUserContextByLoginAsync(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return null;

            string normalized = login.Trim().ToLowerInvariant();

            const string sql = @"
                SELECT TOP(1) userID, SAPuserID, SAPsalesID, fullName, loginID,
                       runReports, scheduleReports, manageReports, administrator
                FROM dbo.reportUsers WHERE LOWER(LTRIM(RTRIM(loginID))) = @loginNormalized;";

            try
            {
                _logger?.LogInformation("GetUserContextByLoginAsync lookup for login='{LoginNormalized}'", normalized);
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new SqlCommand(sql, connection);
                command.Parameters.Add("@loginNormalized", System.Data.SqlDbType.NVarChar, 256).Value = normalized;

                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    var ctx = new UserContext();

                    int ord;
                    ord = reader.GetOrdinal("userID");
                    if (!reader.IsDBNull(ord)) ctx.userID = reader.GetInt32(ord);

                    ord = reader.GetOrdinal("SAPuserID");
                    if (!reader.IsDBNull(ord)) ctx.SAPuserID = reader.GetInt32(ord);

                    ord = reader.GetOrdinal("SAPsalesID");
                    if (!reader.IsDBNull(ord)) ctx.SAPsalesID = reader.GetInt32(ord);

                    ord = reader.GetOrdinal("fullName");
                    if (!reader.IsDBNull(ord)) ctx.fullName = reader.GetString(ord);

                    ord = reader.GetOrdinal("runReports");
                    if (!reader.IsDBNull(ord)) ctx.runReports = reader.GetBoolean(ord);

                    ord = reader.GetOrdinal("scheduleReports");
                    if (!reader.IsDBNull(ord)) ctx.scheduleReports = reader.GetBoolean(ord);

                    ord = reader.GetOrdinal("manageReports");
                    if (!reader.IsDBNull(ord)) ctx.manageReports = reader.GetBoolean(ord);

                    ord = reader.GetOrdinal("administrator");
                    if (!reader.IsDBNull(ord)) ctx.administrator = reader.GetBoolean(ord);

                    _logger?.LogInformation("GetUserContextByLoginAsync found userID={UserID} loginID={Login} fullname={Fullname}", ctx.userID, normalized, ctx.fullName);

                    return ctx;
                }

                _logger?.LogInformation("GetUserContextByLoginAsync found no user for login='{LoginNormalized}'", normalized);
                return null;
            }
            catch
            {
                _logger?.LogWarning("GetUserContextByLoginAsync failed for login='{LoginNormalized}'", normalized);
                return null;
            }
        }

        // Administrative: return list of users and their flags
        public async Task<List<RGBReportingWebsite.Server.Models.UserDetailsDto>> GetAllUsersAsync()
        {
            var results = new List<RGBReportingWebsite.Server.Models.UserDetailsDto>();

            const string sql = @"
                SELECT userID, loginID, fullName, SAPuserID, SAPsalesID, ISNULL(active, 0) AS active,
                       ISNULL(runReports, 0) AS runReports, ISNULL(scheduleReports, 0) AS scheduleReports,
                       ISNULL(manageReports, 0) AS manageReports, ISNULL(administrator, 0) AS administrator
                FROM dbo.reportUsers ORDER BY fullName;";

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new SqlCommand(sql, connection);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var dto = new RGBReportingWebsite.Server.Models.UserDetailsDto();

                    int ord;
                    ord = reader.GetOrdinal("userID");
                    if (!reader.IsDBNull(ord)) dto.userID = reader.GetInt32(ord);

                    ord = reader.GetOrdinal("loginID");
                    if (!reader.IsDBNull(ord)) dto.loginID = reader.GetString(ord);

                    ord = reader.GetOrdinal("fullName");
                    if (!reader.IsDBNull(ord)) dto.fullName = reader.GetString(ord);

                    ord = reader.GetOrdinal("SAPuserID");
                    if (!reader.IsDBNull(ord)) dto.SAPuserID = reader.GetInt32(ord);

                    ord = reader.GetOrdinal("SAPsalesID");
                    if (!reader.IsDBNull(ord)) dto.SAPsalesID = reader.GetInt32(ord);

                    ord = reader.GetOrdinal("active");
                    dto.active = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                    ord = reader.GetOrdinal("runReports");
                    dto.runReports = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                    ord = reader.GetOrdinal("scheduleReports");
                    dto.scheduleReports = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                    ord = reader.GetOrdinal("manageReports");
                    dto.manageReports = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                    ord = reader.GetOrdinal("administrator");
                    dto.administrator = !reader.IsDBNull(ord) && reader.GetBoolean(ord);

                    results.Add(dto);
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetAllUsersAsync failed");
                throw;
            }
        }

        public async Task<bool> UpdateUserFlagsAsync(int userID, bool active, bool runReports, bool scheduleReports, bool manageReports, bool administrator)
        {
            const string sql = @"
                UPDATE dbo.reportUsers
                SET active = @active,
                    runReports = @runReports,
                    scheduleReports = @scheduleReports,
                    manageReports = @manageReports,
                    administrator = @administrator
                WHERE userID = @userID;";

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new SqlCommand(sql, connection);
                command.Parameters.Add("@active", System.Data.SqlDbType.Bit).Value = active;
                command.Parameters.Add("@runReports", System.Data.SqlDbType.Bit).Value = runReports;
                command.Parameters.Add("@scheduleReports", System.Data.SqlDbType.Bit).Value = scheduleReports;
                command.Parameters.Add("@manageReports", System.Data.SqlDbType.Bit).Value = manageReports;
                command.Parameters.Add("@administrator", System.Data.SqlDbType.Bit).Value = administrator;
                command.Parameters.Add("@userID", System.Data.SqlDbType.Int).Value = userID;

                var affected = await command.ExecuteNonQueryAsync();
                return affected > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "UpdateUserFlagsAsync failed for userID={UserID}", userID);
                return false;
            }
        }

        public async Task<List<RGBReportingWebsite.Server.Models.ReportPermissionDto>> GetReportPermissionsAsync(int userID)
        {
            const string sql = @"
                SELECT rd.reportDetailID,
                       rd.category,
                       rd.subCategory,
                       rd.description,
                       ISNULL(rd.enabled, 0) AS reportEnabled,
                       ISNULL(rp.enabled, 0) AS permissionEnabled
                FROM dbo.reportDetails rd
                LEFT JOIN dbo.reportPermissions rp
                    ON rp.reportDetailID = rd.reportDetailID
                    AND rp.userID = @userID
                ORDER BY rd.category, rd.subCategory, rd.description;";

            var results = new List<RGBReportingWebsite.Server.Models.ReportPermissionDto>();

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var command = new SqlCommand(sql, connection);
                command.Parameters.Add("@userID", System.Data.SqlDbType.Int).Value = userID;

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new RGBReportingWebsite.Server.Models.ReportPermissionDto
                    {
                        reportDetailID = reader.GetInt32(reader.GetOrdinal("reportDetailID")),
                        category = reader.IsDBNull(reader.GetOrdinal("category")) ? null : reader.GetString(reader.GetOrdinal("category")),
                        subCategory = reader.IsDBNull(reader.GetOrdinal("subCategory")) ? null : reader.GetString(reader.GetOrdinal("subCategory")),
                        description = reader.IsDBNull(reader.GetOrdinal("description")) ? null : reader.GetString(reader.GetOrdinal("description")),
                        reportEnabled = reader.GetBoolean(reader.GetOrdinal("reportEnabled")),
                        permissionEnabled = reader.GetBoolean(reader.GetOrdinal("permissionEnabled"))
                    });
                }

                return results;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetReportPermissionsAsync failed for userID={UserID}", userID);
                throw;
            }
        }

        public async Task<bool> SetReportPermissionAsync(int userID, int reportDetailID, bool enabled)
        {
            if (userID <= 0 || reportDetailID <= 0)
                return false;

            const string updateSql = @"
                UPDATE dbo.reportPermissions
                SET enabled = @enabled
                WHERE userID = @userID AND reportDetailID = @reportDetailID;";

            const string insertSql = @"
                INSERT INTO dbo.reportPermissions (userID, reportDetailID, enabled)
                VALUES (@userID, @reportDetailID, @enabled);";

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                await using var transaction = await connection.BeginTransactionAsync();
                await using var updateCommand = new SqlCommand(updateSql, connection, (SqlTransaction)transaction);
                updateCommand.Parameters.Add("@enabled", System.Data.SqlDbType.Bit).Value = enabled;
                updateCommand.Parameters.Add("@userID", System.Data.SqlDbType.Int).Value = userID;
                updateCommand.Parameters.Add("@reportDetailID", System.Data.SqlDbType.Int).Value = reportDetailID;

                var affected = await updateCommand.ExecuteNonQueryAsync();
                if (affected == 0)
                {
                    await using var insertCommand = new SqlCommand(insertSql, connection, (SqlTransaction)transaction);
                    insertCommand.Parameters.Add("@enabled", System.Data.SqlDbType.Bit).Value = enabled;
                    insertCommand.Parameters.Add("@userID", System.Data.SqlDbType.Int).Value = userID;
                    insertCommand.Parameters.Add("@reportDetailID", System.Data.SqlDbType.Int).Value = reportDetailID;
                    await insertCommand.ExecuteNonQueryAsync();
                }

                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SetReportPermissionAsync failed for userID={UserID}, reportDetailID={ReportDetailID}", userID, reportDetailID);
                return false;
            }
        }

        public async Task<List<ReportScheduleDto>> GetReportSchedulesAsync()
        {
            const string sql = @"
                SELECT scheduledReportID, reportRunOrder, reportGroupingID, reportName,
                       spreadsheetName, parameter1, parameter1Name, filterCommand,
                       filterCommandName, enabled
                FROM dbo.reportSchedule
                ORDER BY reportRunOrder, reportName, scheduledReportID;";

            var schedules = new List<ReportScheduleDto>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                schedules.Add(new ReportScheduleDto
                {
                    scheduledReportID = reader.GetInt32(reader.GetOrdinal("scheduledReportID")),
                    reportRunOrder = ReadNullableInt(reader, "reportRunOrder"),
                    reportGroupingID = ReadNullableInt(reader, "reportGroupingID"),
                    reportName = ReadNullableString(reader, "reportName"),
                    spreadsheetName = ReadNullableString(reader, "spreadsheetName"),
                    parameter1 = ReadNullableString(reader, "parameter1"),
                    parameter1Name = ReadNullableString(reader, "parameter1Name"),
                    filterCommand = ReadNullableString(reader, "filterCommand"),
                    filterCommandName = ReadNullableString(reader, "filterCommandName"),
                    enabled = reader.IsDBNull(reader.GetOrdinal("enabled"))
                        ? null
                        : reader.GetBoolean(reader.GetOrdinal("enabled"))
                });
            }

            return schedules;
        }

        public async Task<int?> SaveReportScheduleAsync(ReportScheduleDto schedule)
        {
            const string insertSql = @"
                INSERT INTO dbo.reportSchedule
                    (reportRunOrder, reportGroupingID, reportName, spreadsheetName,
                     parameter1, parameter1Name, filterCommand, filterCommandName, enabled)
                OUTPUT INSERTED.scheduledReportID
                VALUES
                    (@reportRunOrder, @reportGroupingID, @reportName, @spreadsheetName,
                     @parameter1, @parameter1Name, @filterCommand, @filterCommandName, @enabled);";
            const string updateSql = @"
                UPDATE dbo.reportSchedule
                SET reportRunOrder = @reportRunOrder,
                    reportGroupingID = @reportGroupingID,
                    reportName = @reportName,
                    spreadsheetName = @spreadsheetName,
                    parameter1 = @parameter1,
                    parameter1Name = @parameter1Name,
                    filterCommand = @filterCommand,
                    filterCommandName = @filterCommandName,
                    enabled = @enabled
                WHERE scheduledReportID = @scheduledReportID;";

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(
                    schedule.scheduledReportID <= 0 ? insertSql : updateSql,
                    connection);
                AddReportScheduleParameters(command, schedule);

                if (schedule.scheduledReportID <= 0)
                {
                    var result = await command.ExecuteScalarAsync();
                    return result is null or DBNull ? null : Convert.ToInt32(result);
                }

                command.Parameters.Add("@scheduledReportID", SqlDbType.Int).Value = schedule.scheduledReportID;
                return await command.ExecuteNonQueryAsync() > 0 ? schedule.scheduledReportID : null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SaveReportScheduleAsync failed for scheduledReportID={ScheduledReportID}", schedule.scheduledReportID);
                return null;
            }
        }

        public async Task<bool> DeleteReportScheduleAsync(int scheduledReportID)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

                await using (var emailsCommand = new SqlCommand(
                    "DELETE FROM dbo.reportScheduleEmails WHERE scheduledReportID = @scheduledReportID;",
                    connection,
                    transaction))
                {
                    emailsCommand.Parameters.Add("@scheduledReportID", SqlDbType.Int).Value = scheduledReportID;
                    await emailsCommand.ExecuteNonQueryAsync();
                }

                await using var scheduleCommand = new SqlCommand(
                    "DELETE FROM dbo.reportSchedule WHERE scheduledReportID = @scheduledReportID;",
                    connection,
                    transaction);
                scheduleCommand.Parameters.Add("@scheduledReportID", SqlDbType.Int).Value = scheduledReportID;
                var affected = await scheduleCommand.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
                return affected > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteReportScheduleAsync failed for scheduledReportID={ScheduledReportID}", scheduledReportID);
                return false;
            }
        }

        public async Task<List<ReportScheduleEmailDto>> GetReportScheduleEmailsAsync(int scheduledReportID)
        {
            const string sql = @"
                SELECT se.reportScheduleEmailID, se.scheduledReportID, se.userID, se.type,
                       se.frequency, se.runTime, se.active, ru.fullName, ru.loginID
                FROM dbo.reportScheduleEmails se
                LEFT JOIN dbo.reportUsers ru ON ru.userID = se.userID
                WHERE se.scheduledReportID = @scheduledReportID
                ORDER BY ru.fullName, se.type, se.frequency;";

            var emails = new List<ReportScheduleEmailDto>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@scheduledReportID", SqlDbType.Int).Value = scheduledReportID;
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var runTimeOrdinal = reader.GetOrdinal("runTime");
                emails.Add(new ReportScheduleEmailDto
                {
                    reportScheduleEmailID = reader.GetInt32(reader.GetOrdinal("reportScheduleEmailID")),
                    scheduledReportID = ReadNullableInt(reader, "scheduledReportID"),
                    userID = ReadNullableInt(reader, "userID"),
                    type = ReadNullableString(reader, "type"),
                    frequency = ReadNullableString(reader, "frequency"),
                    runTime = reader.IsDBNull(runTimeOrdinal) ? null : reader.GetTimeSpan(runTimeOrdinal),
                    active = reader.IsDBNull(reader.GetOrdinal("active"))
                        ? null
                        : reader.GetBoolean(reader.GetOrdinal("active")),
                    fullName = ReadNullableString(reader, "fullName"),
                    loginID = ReadNullableString(reader, "loginID")
                });
            }

            return emails;
        }

        public async Task<int?> SaveReportScheduleEmailAsync(ReportScheduleEmailDto email)
        {
            const string insertSql = @"
                INSERT INTO dbo.reportScheduleEmails
                    (scheduledReportID, userID, type, frequency, runTime, active)
                OUTPUT INSERTED.reportScheduleEmailID
                VALUES (@scheduledReportID, @userID, @type, @frequency, @runTime, @active);";
            const string updateSql = @"
                UPDATE dbo.reportScheduleEmails
                SET scheduledReportID = @scheduledReportID,
                    userID = @userID,
                    type = @type,
                    frequency = @frequency,
                    runTime = @runTime,
                    active = @active
                WHERE reportScheduleEmailID = @reportScheduleEmailID;";

            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(
                    email.reportScheduleEmailID <= 0 ? insertSql : updateSql,
                    connection);
                command.Parameters.Add("@scheduledReportID", SqlDbType.Int).Value = (object?)email.scheduledReportID ?? DBNull.Value;
                command.Parameters.Add("@userID", SqlDbType.Int).Value = (object?)email.userID ?? DBNull.Value;
                AddNullableString(command, "@type", email.type, SqlDbType.NVarChar, 3);
                AddNullableString(command, "@frequency", email.frequency, SqlDbType.NVarChar, 20);
                command.Parameters.Add("@runTime", SqlDbType.Time).Value = (object?)email.runTime ?? DBNull.Value;
                command.Parameters.Add("@active", SqlDbType.Bit).Value = (object?)email.active ?? DBNull.Value;

                if (email.reportScheduleEmailID <= 0)
                {
                    var result = await command.ExecuteScalarAsync();
                    return result is null or DBNull ? null : Convert.ToInt32(result);
                }

                command.Parameters.Add("@reportScheduleEmailID", SqlDbType.Int).Value = email.reportScheduleEmailID;
                return await command.ExecuteNonQueryAsync() > 0 ? email.reportScheduleEmailID : null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "SaveReportScheduleEmailAsync failed for reportScheduleEmailID={EmailID}", email.reportScheduleEmailID);
                return null;
            }
        }

        public async Task<bool> DeleteReportScheduleEmailAsync(int scheduledReportID, int reportScheduleEmailID)
        {
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(
                    "DELETE FROM dbo.reportScheduleEmails WHERE scheduledReportID = @scheduledReportID AND reportScheduleEmailID = @reportScheduleEmailID;",
                    connection);
                command.Parameters.Add("@scheduledReportID", SqlDbType.Int).Value = scheduledReportID;
                command.Parameters.Add("@reportScheduleEmailID", SqlDbType.Int).Value = reportScheduleEmailID;
                return await command.ExecuteNonQueryAsync() > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "DeleteReportScheduleEmailAsync failed for reportScheduleEmailID={EmailID}", reportScheduleEmailID);
                return false;
            }
        }

        public async Task<List<ReportScheduleHistoryDto>> GetReportScheduleHistoryAsync(int scheduledReportID)
        {
            const string sql = @"
                SELECT reportScheduleHistoryID, reportScheduleHistoryDate, scheduledReportID,
                       toEmailAddress, ccEmailAddress, BccEmailAddress, successful, errorMessage
                FROM dbo.reportScheduleHistory
                WHERE scheduledReportID = @scheduledReportID
                ORDER BY reportScheduleHistoryDate DESC, reportScheduleHistoryID DESC;";

            var history = new List<ReportScheduleHistoryDto>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.Add("@scheduledReportID", SqlDbType.Int).Value = scheduledReportID;
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var dateOrdinal = reader.GetOrdinal("reportScheduleHistoryDate");
                var successfulOrdinal = reader.GetOrdinal("successful");
                history.Add(new ReportScheduleHistoryDto
                {
                    reportScheduleHistoryID = reader.GetInt32(reader.GetOrdinal("reportScheduleHistoryID")),
                    reportScheduleHistoryDate = reader.IsDBNull(dateOrdinal) ? null : reader.GetDateTime(dateOrdinal),
                    scheduledReportID = ReadNullableInt(reader, "scheduledReportID"),
                    toEmailAddress = ReadNullableString(reader, "toEmailAddress"),
                    ccEmailAddress = ReadNullableString(reader, "ccEmailAddress"),
                    BccEmailAddress = ReadNullableString(reader, "BccEmailAddress"),
                    successful = reader.IsDBNull(successfulOrdinal) ? null : reader.GetBoolean(successfulOrdinal),
                    errorMessage = ReadNullableString(reader, "errorMessage")
                });
            }

            return history;
        }

        public async Task<List<string>> GetActiveReportFrequenciesAsync()
        {
            const string sql = @"
                SELECT DISTINCT frequency
                FROM dbo.reportFrequencies
                WHERE active = 1 AND frequency IS NOT NULL
                ORDER BY frequency;";

            var frequencies = new List<string>();
            try
            {
                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(sql, connection);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    frequencies.Add(reader.GetString(0));

                return frequencies;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "GetActiveReportFrequenciesAsync failed");
                throw;
            }
        }

        private static void AddReportScheduleParameters(SqlCommand command, ReportScheduleDto schedule)
        {
            command.Parameters.Add("@reportRunOrder", SqlDbType.Int).Value = (object?)schedule.reportRunOrder ?? DBNull.Value;
            command.Parameters.Add("@reportGroupingID", SqlDbType.Int).Value = (object?)schedule.reportGroupingID ?? DBNull.Value;
            AddNullableString(command, "@reportName", schedule.reportName, SqlDbType.VarChar, 70);
            AddNullableString(command, "@spreadsheetName", schedule.spreadsheetName, SqlDbType.VarChar, 70);
            AddNullableString(command, "@parameter1", schedule.parameter1, SqlDbType.VarChar, 50);
            AddNullableString(command, "@parameter1Name", schedule.parameter1Name, SqlDbType.VarChar, 50);
            AddNullableString(command, "@filterCommand", schedule.filterCommand, SqlDbType.VarChar, 255);
            AddNullableString(command, "@filterCommandName", schedule.filterCommandName, SqlDbType.VarChar, 50);
            command.Parameters.Add("@enabled", SqlDbType.Bit).Value = (object?)schedule.enabled ?? DBNull.Value;
        }

        private static void AddNullableString(SqlCommand command, string name, string? value, SqlDbType type, int size)
        {
            command.Parameters.Add(name, type, size).Value = (object?)value ?? DBNull.Value;
        }

        private static int? ReadNullableInt(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }

        private static string? ReadNullableString(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }
    }
}