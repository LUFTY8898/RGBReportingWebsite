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
            var reports =
                await GetReportDetailsAsync(userID);

            return reports.FirstOrDefault(
                r => r.reportDetailID == reportDetailID);
        }

        // Get SAP ids and user info by login (azure email or loginID)
        public async Task<UserContext?> GetUserContextByLoginAsync(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return null;

            string normalized = login.Trim().ToLowerInvariant();

            const string sql = @"
                SELECT TOP(1) userID, SAPuserID, SAPsalesID, fullName, loginID
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
    }
}