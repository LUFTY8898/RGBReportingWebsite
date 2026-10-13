using Microsoft.Data.SqlClient;
using System.Data;
using System.Linq;

namespace RGBReportingWebsite.Server.Services
{
    public class DatasetService
    {
        private readonly string _websiteConnectionString;
        private readonly string _rgbReportingConnectionString;
        public DatasetService(
            IConfiguration configuration)
        {
            _websiteConnectionString =
                configuration.GetConnectionString("Website")
                ?? throw new InvalidOperationException("Website database connection string not found.");

            _rgbReportingConnectionString =
                configuration.GetConnectionString("RGB_Reporting")
                ?? throw new InvalidOperationException("RGB_Reporting database connection string not found.");

            if (string.IsNullOrWhiteSpace(_websiteConnectionString))
            {
                throw new InvalidOperationException("Website database connection string not found.");
            }

            if (string.IsNullOrWhiteSpace(_rgbReportingConnectionString))
            {
                throw new InvalidOperationException("RGB_Reporting database connection string not found.");
            }
        }

        // Return list of account manager options for dropdown
        // Each item: { value = int? userID, text = fullName }
        public async Task<List<Dictionary<string, object?>>> GetAccountManagersAsync(int loggedInUserID, int reportDetailID)
        {
            // Legacy entrypoint preserved: delegate to the new dropdown-focused method.
            return await GetAccountManagersForDropdownAsync(loggedInUserID, reportDetailID);
        }

        // New method: return account manager options keyed by reportUsers.userID
        public async Task<List<Dictionary<string, object?>>> GetAccountManagersForDropdownAsync(int loggedInUserID, int reportDetailID)
        {
            var results = new List<Dictionary<string, object?>>();

            if (reportDetailID <= 0)
                return results;
                        
            int? loggedUserID = null;
            string? loggedUserFullName = null;
            int? loggedUserSAPsalesID = null;

            try
            {
                await using (var conn = new SqlConnection(_rgbReportingConnectionString))
                {
                    await conn.OpenAsync();

                    // Resolve logged-in user
                    const string findUserSql = @"SELECT TOP(1) userID, SAPsalesID, fullName, loginID FROM dbo.reportUsers WHERE (userID = @loggedInUserID);";

                    await using (var cmd = new SqlCommand(findUserSql, conn))
                    {
                        cmd.Parameters.Add("@loggedInUserID", System.Data.SqlDbType.Int).Value = loggedInUserID;

                        await using var rdr = await cmd.ExecuteReaderAsync();
                        if (await rdr.ReadAsync())
                        {
                            if (!rdr.IsDBNull(rdr.GetOrdinal("userID")))
                                loggedUserID = rdr.GetInt32(rdr.GetOrdinal("userID"));

                            if (!rdr.IsDBNull(rdr.GetOrdinal("SAPsalesID")))
                                loggedUserSAPsalesID = rdr.GetInt32(rdr.GetOrdinal("SAPsalesID"));

                            if (!rdr.IsDBNull(rdr.GetOrdinal("fullName")))
                                loggedUserFullName = rdr.GetString(rdr.GetOrdinal("fullName"));
                        }
                    }

                    // Determine allowed SAP IDs from authorisations
                    var allowedSapIds = new List<int>();

                    const string authSql = @"SELECT DISTINCT SAPuserIDAllowed FROM dbo.reportAccountManagerAuthorisations WHERE reportID = @reportID AND userID = @userID;";
                        
                    await using (var authCmd = new SqlCommand(authSql, conn))
                    {
                        authCmd.Parameters.Add("@reportID", System.Data.SqlDbType.Int).Value = reportDetailID;
                        authCmd.Parameters.Add("@userID", System.Data.SqlDbType.Int).Value = loggedInUserID;
                      
                        await using var authRdr = await authCmd.ExecuteReaderAsync();
                        while (await authRdr.ReadAsync())
                        {
                            if (!authRdr.IsDBNull(authRdr.GetOrdinal("SAPuserIDAllowed")))
                                allowedSapIds.Add(authRdr.GetInt32(authRdr.GetOrdinal("SAPuserIDAllowed")));
                        }
                    }

                    if (!allowedSapIds.Any())
                    {
                        // No authorisations found. As a fallback, include the logged-in user
                        // so the dropdown at least shows the current user's fullName if available.
                        if (loggedUserID.HasValue)
                        {
                            results.Add(new Dictionary<string, object?> { ["value"] = loggedUserID.Value, ["text"] = loggedUserFullName ?? string.Empty });
                            return results;
                        }

                        return results;
                    }

                    bool includeAllOption = allowedSapIds.Contains(0);

                    var users = new List<(int userID, string fullName)>();

                    if (includeAllOption)
                    {
                        // All active users with SAPsalesID > 0
                        const string allSql = @"SELECT userID, fullName FROM dbo.reportUsers WHERE active = 1 AND SAPsalesID > 0 ORDER BY fullName;";
                        await using (var allCmd = new SqlCommand(allSql, conn))
                        {
                            await using var allRdr = await allCmd.ExecuteReaderAsync();
                            while (await allRdr.ReadAsync())
                            {
                                var uid = allRdr.IsDBNull(allRdr.GetOrdinal("userID")) ? 0 : allRdr.GetInt32(allRdr.GetOrdinal("userID"));
                                var name = allRdr.IsDBNull(allRdr.GetOrdinal("fullName")) ? string.Empty : allRdr.GetString(allRdr.GetOrdinal("fullName"));
                                if (uid != 0)
                                    users.Add((uid, name));
                            }
                        }
                    }
                    else
                    {
                        // Filter by allowed SAPsalesID values
                        var distinctIds = allowedSapIds.Distinct().ToList();
                        var paramNames = distinctIds.Select((v, i) => "@p" + i).ToList();
                        var inClause = string.Join(",", paramNames);
                        var sql = $"SELECT userID, fullName FROM dbo.reportUsers WHERE active = 1 AND SAPsalesID IN ({inClause}) ORDER BY fullName;";

                        await using (var qcmd = new SqlCommand(sql, conn))
                        {
                            for (int i = 0; i < distinctIds.Count; i++)
                            {
                                qcmd.Parameters.Add(paramNames[i], System.Data.SqlDbType.Int).Value = distinctIds[i];
                            }

                            await using var qRdr = await qcmd.ExecuteReaderAsync();
                            while (await qRdr.ReadAsync())
                            {
                                var uid = qRdr.IsDBNull(qRdr.GetOrdinal("userID")) ? 0 : qRdr.GetInt32(qRdr.GetOrdinal("userID"));
                                var name = qRdr.IsDBNull(qRdr.GetOrdinal("fullName")) ? string.Empty : qRdr.GetString(qRdr.GetOrdinal("fullName"));
                                if (uid != 0)
                                    users.Add((uid, name));
                            }
                        }
                    }

                    // Ensure logged-in user appears first
                    if (loggedUserID.HasValue)
                    {
                        var idx = users.FindIndex(u => u.userID == loggedUserID.Value);
                        if (idx > 0)
                        {
                            var item = users[idx];
                            users.RemoveAt(idx);
                            users.Insert(0, item);
                        }
                        else if (idx == -1)
                        {
                            // Insert logged-in user at top (fetch fullName if missing)
                            var name = loggedUserFullName ?? string.Empty;
                            if (string.IsNullOrWhiteSpace(name))
                            {
                                const string singleSql = @"SELECT fullName FROM dbo.reportUsers WHERE userID = @userID;";
                                await using (var sCmd = new SqlCommand(singleSql, conn))
                                {
                                    sCmd.Parameters.Add("@userID", System.Data.SqlDbType.Int).Value = loggedUserID.Value;
                                    var obj = await sCmd.ExecuteScalarAsync();
                                    if (obj != null && obj != System.DBNull.Value)
                                        name = obj.ToString() ?? string.Empty;
                                }
                            }

                            users.Insert(0, (loggedUserID.Value, name));
                        }
                    }

                    // Build results
                    foreach (var u in users)
                    {
                        results.Add(new Dictionary<string, object?> { ["value"] = u.userID, ["text"] = u.fullName });
                    }
                }
            }
            catch
            {
                // on error return what we have (empty)
            }

            return results;
        }


        // ============================================================
        // PUBLIC ENTRY POINT
        // ============================================================

        public async Task<DataTable> GenerateDatasetAsync(
           string datasetName,
           int reportDetailID,
           string filterString = "",
           int manufacturerID = 0,
           string cardCode = "",
           string? manufacturerField = null,
           int? manufacturerFirmCode = null,
           string? dateRangeField = null,
           DateTime? fromDate = null,
           DateTime? toDate = null,
           string? accountManagerValue = null)
        
        {
            if (string.IsNullOrWhiteSpace(datasetName))
            {
                throw new ArgumentException(
                    "A dataset name must be supplied.",
                    nameof(datasetName));
            }


            if (reportDetailID <= 0)
            {
                throw new ArgumentException(
                    "A valid reportDetailID must be supplied.",
                    nameof(reportDetailID));
            }


            datasetName =
                datasetName.Trim();


            DataSet workingData =
                new DataSet();


            // --------------------------------------------------------
            // Load dataset metadata
            // --------------------------------------------------------

            await LoadDatasetDefinitionsAsync(
                workingData);


            // --------------------------------------------------------
            // Create genericData structure
            // --------------------------------------------------------

            CreateDataset(
                workingData,
                "genericData",
                datasetName);


            // --------------------------------------------------------
            // Populate genericData
            // --------------------------------------------------------

            await PopulateDatasetAsync(
                workingData,
                "genericData",
                datasetName,
                reportDetailID,
                filterString,
                manufacturerID,
                cardCode,
                manufacturerField,
                manufacturerFirmCode,
                dateRangeField,
                fromDate,
                toDate,
                accountManagerValue: accountManagerValue);



            return workingData.Tables["genericData"]
                ?? throw new InvalidOperationException(
                    "The generated dataset 'genericData' was not created.");
        }


        // ============================================================
        // LOAD DATASET DEFINITIONS
        // ============================================================

        private async Task LoadDatasetDefinitionsAsync(
            DataSet data)
        {
            await using SqlConnection connection =
                new SqlConnection(
                    _websiteConnectionString);


            await connection.OpenAsync();


            // --------------------------------------------------------
            // Dataset definitions
            //
            // Physical SQL table:
            //     dbo.reportDatasets
            //
            // Internal DataTable:
            //     datasets
            // --------------------------------------------------------

            const string datasetsSql = @"
                SELECT *
                FROM dbo.reportDatasets;";


            await using (
                SqlCommand command =
                    new SqlCommand(
                        datasetsSql,
                        connection))
            {
                command.CommandTimeout =
                    3600;


                await using SqlDataReader reader =
                    await command.ExecuteReaderAsync();


                DataTable datasets =
                    new DataTable(
                        "datasets");


                datasets.Load(
                    reader);


                data.Tables.Add(
                    datasets);
            }


            // --------------------------------------------------------
            // Dataset field definitions
            //
            // sourceFieldName
            //     = SQL source/expression
            //
            // fieldName
            //     = output DataTable column name
            // --------------------------------------------------------

            const string fieldsSql = @"
                SELECT *
                FROM dbo.reportDatasetFieldDefinitions;";


            await using (
                SqlCommand command =
                    new SqlCommand(
                        fieldsSql,
                        connection))
            {
                command.CommandTimeout =
                    3600;


                await using SqlDataReader reader =
                    await command.ExecuteReaderAsync();


                DataTable fields =
                    new DataTable(
                        "datasetFieldDefinitions");


                fields.Load(
                    reader);


                data.Tables.Add(
                    fields);
            }
        }


        // ============================================================
        // CREATE DATASET
        //
        // fieldName = output DataTable column name
        // ============================================================

        private static void CreateDataset(
            DataSet data,
            string tableName,
            string definitionName)
        {
            if (string.IsNullOrWhiteSpace(
                    definitionName))
            {
                throw new InvalidOperationException(
                    "The dataset definition name is empty.");
            }


            definitionName =
                definitionName.Trim();


            DataTable definitions =
                data.Tables[
                    "datasetFieldDefinitions"]
                ?? throw new InvalidOperationException(
                    "datasetFieldDefinitions has not been loaded.");


            // --------------------------------------------------------
            // Validate required fields
            // --------------------------------------------------------

            string[] requiredColumns =
            {
                "datasetName",
                "fieldOrder",
                "fieldName",
                "sourceFieldName",
                "fieldTypeID",
                "includeField",
                "populateField"
            };


            foreach (string columnName
                     in requiredColumns)
            {
                if (!definitions
                        .Columns
                        .Contains(
                            columnName))
                {
                    throw new InvalidOperationException(
                        "datasetFieldDefinitions does not contain " +
                        $"the required column '{columnName}'.");
                }
            }


            // --------------------------------------------------------
            // Get fields for requested dataset
            // --------------------------------------------------------

            List<DataRow> fields =
                definitions
                    .AsEnumerable()
                    .Where(row =>
                        string.Equals(
                            GetString(
                                row,
                                "datasetName"),
                            definitionName,
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(row =>
                        GetInt(
                            row,
                            "fieldOrder",
                            int.MaxValue))
                    .ToList();


            if (fields.Count == 0)
            {
                throw new InvalidOperationException(
                    "No dataset field definitions were found " +
                    $"for dataset '{definitionName}'.");
            }


            // --------------------------------------------------------
            // Remove existing destination
            // --------------------------------------------------------

            if (data.Tables.Contains(
                    tableName))
            {
                data.Tables.Remove(
                    tableName);
            }


            DataTable table =
                new DataTable(
                    tableName);


            // --------------------------------------------------------
            // Create output columns
            // --------------------------------------------------------

            foreach (DataRow field in fields)
            {
                bool includeField =
                    GetBool(
                        field,
                        "includeField");


                if (!includeField)
                {
                    continue;
                }


                string fieldName =
                    GetString(
                        field,
                        "fieldName");


                if (string.IsNullOrWhiteSpace(
                        fieldName))
                {
                    continue;
                }


                int fieldTypeID =
                    GetInt(
                        field,
                        "fieldTypeID",
                        2);


                Type fieldType =
                    GetFieldType(
                        fieldTypeID);


                if (table.Columns.Contains(
                        fieldName))
                {
                    throw new InvalidOperationException(
                        $"Dataset '{definitionName}' contains " +
                        $"duplicate output field '{fieldName}'.");
                }


                table.Columns.Add(
                    fieldName,
                    fieldType);
            }


            if (table.Columns.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Dataset '{definitionName}' " +
                    "contains no included fields.");
            }


            data.Tables.Add(
                table);
        }


        // ============================================================
        // LOAD REPORT COLUMN DEFINITIONS
        //
        // reportColumnNamesDetail controls report-specific sorting.
        // ============================================================

        private async Task<DataTable> LoadReportColumnNamesAsync(
            int reportDetailID)
        {
            DataTable table =
                new DataTable(
                    "reportColumnNames");


            const string sql = @"
                SELECT *
                FROM dbo.reportColumnNamesDetail
                WHERE reportDetailID = @reportDetailID;";


            await using SqlConnection connection =
                new SqlConnection(
                    _websiteConnectionString);


            await connection.OpenAsync();


            await using SqlCommand command =
                new SqlCommand(
                    sql,
                    connection);


            command.CommandTimeout =
                3600;


            command.Parameters.Add(
                "@reportDetailID",
                SqlDbType.Int)
                .Value =
                reportDetailID;


            await using SqlDataReader reader =
                await command
                    .ExecuteReaderAsync();


            table.Load(
                reader);


            return table;
        }


        // ============================================================
        // POPULATE DATASET
        // ============================================================

        private async Task PopulateDatasetAsync(
             DataSet data,
             string tableToPopulate,
             string datasetName,
             int reportDetailID,
             string filterString,
             int manufacturerID,
             string cardCode,
             string? manufacturerField,
             int? manufacturerFirmCode,
             string? dateRangeField,
             DateTime? fromDate,
             DateTime? toDate,
             string? accountManagerValue = null)

        {
            DataTable datasets =
                data.Tables["datasets"]
                ?? throw new InvalidOperationException(
                    "datasets table has not been loaded.");

            DataTable fieldDefinitions =
                data.Tables[
                    "datasetFieldDefinitions"]
                ?? throw new InvalidOperationException(
                    "datasetFieldDefinitions has not been loaded.");


            // --------------------------------------------------------
            // Report-specific column configuration
            // --------------------------------------------------------

            DataTable reportColumnNames =
                await LoadReportColumnNamesAsync(
                    reportDetailID);


            // --------------------------------------------------------
            // Find dataset definition
            // --------------------------------------------------------

            DataRow? datasetDefinition =
                datasets
                    .AsEnumerable()
                    .FirstOrDefault(row =>
                        string.Equals(
                            GetString(
                                row,
                                "datasetName"),
                            datasetName.Trim(),
                            StringComparison.OrdinalIgnoreCase));


            if (datasetDefinition == null)
            {
                throw new InvalidOperationException(
                    $"Dataset definition '{datasetName}' was not found.");
            }


            // --------------------------------------------------------
            // Dataset configuration
            // --------------------------------------------------------

            string baseLocation =
                GetString(
                    datasetDefinition,
                    "baseLocation");


            string baseTable =
                GetString(
                    datasetDefinition,
                    "baseTable");


            string joinSQL =
                GetString(
                    datasetDefinition,
                    "joinSQL");


            string filterSQL =
                GetString(
                    datasetDefinition,
                    "filterSQL");


            string groupBySQL =
                GetString(
                    datasetDefinition,
                    "groupBySQL");


            // --------------------------------------------------------
            // Choose source database
            // --------------------------------------------------------

            string connectionString =
                baseLocation.Equals(
                    "Website",
                    StringComparison.OrdinalIgnoreCase)
                    ? _websiteConnectionString
                    : _rgbReportingConnectionString;


            // --------------------------------------------------------
            // Dataset fields
            // --------------------------------------------------------

            List<DataRow> fields =
                fieldDefinitions
                    .AsEnumerable()
                    .Where(row =>
                        string.Equals(
                            GetString(
                                row,
                                "datasetName"),
                            datasetName.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(row =>
                        GetInt(
                            row,
                            "fieldOrder",
                            int.MaxValue))
                    .ToList();


            if (fields.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No field definitions were found " +
                    $"for dataset '{datasetName}'.");
            }


            // ========================================================
            // BUILD SELECT LIST
            //
            // fieldName
            //     = output column / alias
            //
            // sourceFieldName
            //     = SQL source/expression
            // ========================================================

            List<string> selectFields =
                new List<string>();


            foreach (DataRow field in fields)
            {
                bool includeField =
                    GetBool(
                        field,
                        "includeField");


                bool populateField =
                    GetBool(
                        field,
                        "populateField");


                if (!includeField ||
                    !populateField)
                {
                    continue;
                }


                string fieldName =
                    GetString(
                        field,
                        "fieldName");


                string sourceFieldName =
                    GetString(
                        field,
                        "sourceFieldName");


                string fixedValue =
                    GetString(
                        field,
                        "fixedValue");


                int fieldTypeID =
                    GetInt(
                        field,
                        "fieldTypeID",
                        2);


                if (string.IsNullOrWhiteSpace(
                        fieldName))
                {
                    continue;
                }


                // ----------------------------------------------------
                // Fixed value
                // ----------------------------------------------------

                if (!string.IsNullOrWhiteSpace(
                        fixedValue))
                {
                    if (IsNumericOrBooleanField(
                            fieldTypeID))
                    {
                        selectFields.Add(
                            $"{fixedValue} AS [{fieldName}]");
                    }
                    else
                    {
                        string safeValue =
                            fixedValue.Replace(
                                "'",
                                "''");


                        selectFields.Add(
                            $"'{safeValue}' AS [{fieldName}]");
                    }


                    continue;
                }


                // ----------------------------------------------------
                // SQL source/expression
                // ----------------------------------------------------

                if (!string.IsNullOrWhiteSpace(
                        sourceFieldName))
                {
                    selectFields.Add(
                        $"{sourceFieldName} AS [{fieldName}]");


                    continue;
                }


                // ----------------------------------------------------
                // Use output field itself if no SQL expression exists
                // ----------------------------------------------------

                selectFields.Add(
                    $"[{fieldName}]");
            }


            if (selectFields.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Dataset '{datasetName}' " +
                    "contains no fields to populate.");
            }


            // ========================================================
            // BASE TABLE
            // ========================================================

            if (string.IsNullOrWhiteSpace(
                    baseTable))
            {
                baseTable =
                    "RGBSAP_" +
                    datasetName;
            }


            // ========================================================
            // BUILD SQL
            // ========================================================

            string sql =
                "SELECT " +
                string.Join(
                    ", ",
                    selectFields) +
                " FROM " +
                baseTable;


            // ========================================================
            // JOIN
            // ========================================================

            if (!string.IsNullOrWhiteSpace(
                    joinSQL))
            {
                sql +=
                    " " +
                    joinSQL;
            }


            // ========================================================
            // WHERE
            // ========================================================

            // account manager SAPsalesID if resolved
            int? accountManagerSAPsalesID = null;

            List<string> filters =
                new List<string>();


            if (!string.IsNullOrWhiteSpace(
                    filterString))
            {
                filters.Add(
                    filterString);
            }


            if (!string.IsNullOrWhiteSpace(
                    filterSQL))
            {
                filters.Add(
                    filterSQL);
            }


            // --------------------------------------------------------
            // Manufacturer
            //
            // manufacturerField:
            //     reportDetails.manufacturerSelect
            //
            // manufacturerFirmCode:
            //     selected OMRC.FirmCode
            //
            // Null manufacturerFirmCode = All Manufacturers.
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(
                    manufacturerField) &&
                manufacturerFirmCode.HasValue)
            {
                string validatedManufacturerField =
                    ValidateSqlFieldName(
                        manufacturerField);


                filters.Add(
                    $"{validatedManufacturerField} " +
                    "= @ManufacturerFirmCode");
            }

            // --------------------------------------------------------
            // Date range filter
            //
            // dateRangeField comes from:
            //
            //     reportDetails.dateRangeSelect
            //
            // Example:
            //
            //     T0.DocDate
            //
            // From Date is inclusive.
            //
            // To Date is exclusive of the following day so that
            // datetime values throughout the selected To Date
            // are included.
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(
                    dateRangeField) &&
                fromDate.HasValue &&
                toDate.HasValue)
            {
                string validatedDateRangeField =
                    ValidateSqlFieldName(
                        dateRangeField);

                filters.Add(
                    $"{validatedDateRangeField} >= @FromDate");

                filters.Add(
                    $"{validatedDateRangeField} < " +
                    "DATEADD(DAY, 1, @ToDate)");
            }

            // --------------------------------------------------------
            // Account manager filter
            // If the reportDetails.accountManagerSelect is configured, and
            // the client supplied an accountManagerValue, look up the
            // corresponding SAPsalesID from dbo.reportUsers using the
            // configured field name, then filter dataset rows where
            // SAPsalesID = found value.
            // --------------------------------------------------------
            if (!string.IsNullOrWhiteSpace(accountManagerValue))
            {
                try
                {
                    string? accountManagerField = null;

                    const string amSql = @"
                        SELECT TOP(1) accountManagerSelect
                        FROM dbo.reportDetails
                        WHERE reportDetailID = @reportDetailID;";

                    await using (SqlConnection amConn = new SqlConnection(_rgbReportingConnectionString))
                    {
                        await amConn.OpenAsync();
                        await using (SqlCommand amCmd = new SqlCommand(amSql, amConn))
                        {
                            amCmd.Parameters.Add("@reportDetailID", SqlDbType.Int).Value = reportDetailID;
                            var amResult = await amCmd.ExecuteScalarAsync();
                            if (amResult != null && amResult != DBNull.Value)
                            {
                                accountManagerField = amResult.ToString();
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(accountManagerField))
                    {
                        string validatedAccountManagerField = ValidateSqlFieldName(accountManagerField!);

                        // Lookup SAPsalesID from reportUsers table using the configured field
                        string lookupSql = $"SELECT TOP(1) SAPsalesID FROM dbo.reportUsers WHERE LOWER(LTRIM(RTRIM([{validatedAccountManagerField}]))) = @amValue;";

                        await using (SqlConnection lookupConn = new SqlConnection(_rgbReportingConnectionString))
                        {
                            await lookupConn.OpenAsync();
                            await using (SqlCommand lookupCmd = new SqlCommand(lookupSql, lookupConn))
                            {
                                string normalized = accountManagerValue.Trim().ToLowerInvariant();
                                lookupCmd.Parameters.Add("@amValue", SqlDbType.NVarChar, 256).Value = normalized;
                                var sapResult = await lookupCmd.ExecuteScalarAsync();
                                if (sapResult != null && sapResult != DBNull.Value)
                                {
                                    if (int.TryParse(sapResult.ToString(), out int SAPsalesID))
                                    {
                                        // Add filter comparing dataset SAPsalesID to the looked-up value
                                        filters.Add("[SAPsalesID] = @AccountManagerSAPsalesID");

                                        // Remember to add parameter later when creating the command
                                        // We'll add a marker variable to pass the value down.
                                        accountManagerSAPsalesID = SAPsalesID;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Do not fail dataset generation for account manager lookup issues
                    Console.WriteLine($"Warning: account manager lookup failed - {ex.Message}");
                }
            }


            if (filters.Count > 0)
            {
                sql +=
                    " WHERE " +
                    string.Join(
                        " AND ",
                        filters);
            }


            // ========================================================
            // GROUP BY
            // ========================================================

            if (!string.IsNullOrWhiteSpace(
                    groupBySQL))
            {
                sql +=
                    " GROUP BY " +
                    groupBySQL;
            }


            // ========================================================
            // ORDER BY
            //
            // Sorting comes from reportColumnNamesDetail.
            //
            // columnName must match the generated output column.
            // ========================================================

            List<DataRow> sortFields =
                reportColumnNames
                    .AsEnumerable()
                    .Where(row =>
                        GetInt(
                            row,
                            "sortOrder",
                            0) > 0)
                    .OrderBy(row =>
                        GetInt(
                            row,
                            "sortOrder"))
                    .ToList();


            if (sortFields.Count > 0)
            {
                List<string> orderFields =
                    new List<string>();


                foreach (DataRow sortField
                         in sortFields)
                {
                    string columnName =
                        GetString(
                            sortField,
                            "columnName");


                    if (string.IsNullOrWhiteSpace(
                            columnName))
                    {
                        continue;
                    }


                    bool sortAscending =
                        GetBool(
                            sortField,
                            "sortAscending",
                            true);


                    string direction =
                        sortAscending
                            ? "ASC"
                            : "DESC";


                    orderFields.Add(
                        $"[{columnName}] {direction}");
                }


                if (orderFields.Count > 0)
                {
                    sql +=
                        " ORDER BY " +
                        string.Join(
                            ", ",
                            orderFields);
                }
            }


            // ========================================================
            // EXECUTE
            // ========================================================

            await using SqlConnection connection =
                new SqlConnection(
                    connectionString);


            await connection.OpenAsync();


            await using SqlCommand command =
                new SqlCommand(
                    sql,
                    connection);


            command.CommandTimeout =
                3600;


            // --------------------------------------------------------
            // Manufacturer parameter
            //
            // The FirmCode value is never concatenated into SQL.
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(
                    manufacturerField) &&
                manufacturerFirmCode.HasValue)
            {
                command.Parameters.Add(
                    "@ManufacturerFirmCode",
                    SqlDbType.Int)
                    .Value =
                    manufacturerFirmCode.Value;
            }

            // --------------------------------------------------------
            // Date parameters
            // --------------------------------------------------------

            if (!string.IsNullOrWhiteSpace(
                    dateRangeField) &&
                fromDate.HasValue &&
                toDate.HasValue)
            {
                command.Parameters.Add(
                    "@FromDate",
                    SqlDbType.DateTime)
                    .Value =
                    fromDate.Value.Date;

                command.Parameters.Add(
                    "@ToDate",
                    SqlDbType.DateTime)
                    .Value =
                    toDate.Value.Date;
            }

            // --------------------------------------------------------
            // Account manager parameter (resolved SAPsalesID)
            // --------------------------------------------------------
            if (accountManagerSAPsalesID.HasValue)
            {
                command.Parameters.Add(
                    "@AccountManagerSAPsalesID",
                    SqlDbType.Int)
                    .Value = accountManagerSAPsalesID.Value;
            }

            SqlDataReader reader;


            try
            {
                reader =
                    await command
                        .ExecuteReaderAsync();
            }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    "Generated SQL failed." +
                    Environment.NewLine +
                    Environment.NewLine +
                    "SQL:" +
                    Environment.NewLine +
                    sql +
                    Environment.NewLine +
                    Environment.NewLine +
                    "SQL Server Error:" +
                    Environment.NewLine +
                    ex.Message,
                    ex);
            }


            // ========================================================
            // LOAD RESULT
            // ========================================================

            await using (reader)
            {
                DataTable destination =
                    data.Tables[
                        tableToPopulate]
                    ?? throw new InvalidOperationException(
                        $"Table '{tableToPopulate}' does not exist.");


                destination.Clear();

                try
                {
                    destination.Load(reader);
                }
                catch (Exception ex)
                {
                    throw new Exception("SQL:" + sql + " " + ex.ToString());
                }
            }
        }


        // ============================================================
        // VALIDATE SQL FIELD NAME
        //
        // The selected FirmCode can be parameterised.
        //
        // The configured column/expression name cannot be represented
        // by a SqlParameter, so only safe SQL identifier characters
        // are accepted here.
        //
        // Accepted examples:
        //
        // FirmCode
        // ManufacturerID
        // T0.FirmCode
        // [FirmCode]
        // T0.[FirmCode]
        // ============================================================

        private static string ValidateSqlFieldName(
            string fieldName)
        {
            fieldName =
                fieldName.Trim();


            if (string.IsNullOrWhiteSpace(
                    fieldName))
            {
                throw new InvalidOperationException(
                    "Manufacturer field name is empty.");
            }


            foreach (char character
                     in fieldName)
            {
                if (!char.IsLetterOrDigit(
                        character) &&
                    character != '_' &&
                    character != '.' &&
                    character != '[' &&
                    character != ']')
                {
                    throw new InvalidOperationException(
                        $"Invalid manufacturer field name " +
                        $"'{fieldName}'.");
                }
            }


            return fieldName;
        }


        // ============================================================
        // FIELD TYPE MAPPING
        // ============================================================

        private static Type GetFieldType(
            int fieldTypeID)
        {
            return fieldTypeID switch
            {
                1 => typeof(int),

                2 => typeof(string),

                3 => typeof(bool),

                4 => typeof(decimal),

                5 => typeof(DateTime),

                6 => typeof(double),

                7 => typeof(string),

                8 => typeof(TimeSpan),

                _ => typeof(string)
            };
        }


        // ============================================================
        // NUMERIC / BOOLEAN
        // ============================================================

        private static bool IsNumericOrBooleanField(
            int fieldTypeID)
        {
            return
                fieldTypeID == 1 ||
                fieldTypeID == 3 ||
                fieldTypeID == 4 ||
                fieldTypeID == 6;
        }


        // ============================================================
        // DATAROW STRING
        // ============================================================

        private static string GetString(
            DataRow row,
            string columnName)
        {
            if (!row.Table.Columns.Contains(
                    columnName))
            {
                return string.Empty;
            }


            if (row.IsNull(
                    columnName))
            {
                return string.Empty;
            }


            return Convert
                .ToString(
                    row[columnName])
                ?.Trim()
                ?? string.Empty;
        }


        // ============================================================
        // DATAROW INTEGER
        // ============================================================

        private static int GetInt(
            DataRow row,
            string columnName,
            int defaultValue = 0)
        {
            if (!row.Table.Columns.Contains(
                    columnName))
            {
                return defaultValue;
            }


            if (row.IsNull(
                    columnName))
            {
                return defaultValue;
            }


            if (int.TryParse(
                    Convert.ToString(
                        row[columnName]),
                    out int result))
            {
                return result;
            }


            return defaultValue;
        }


        // ============================================================
        // DATAROW BOOLEAN
        // ============================================================

        private static bool GetBool(
            DataRow row,
            string columnName,
            bool defaultValue = false)
        {
            if (!row.Table.Columns.Contains(
                    columnName))
            {
                return defaultValue;
            }


            if (row.IsNull(
                    columnName))
            {
                return defaultValue;
            }


            object value =
                row[columnName];


            if (value is bool boolValue)
            {
                return boolValue;
            }


            if (value is int intValue)
            {
                return intValue != 0;
            }


            if (bool.TryParse(
                    Convert.ToString(
                        value),
                    out bool boolResult))
            {
                return boolResult;
            }


            if (int.TryParse(
                    Convert.ToString(
                        value),
                    out int intResult))
            {
                return intResult != 0;
            }


            return defaultValue;
        }
    }
}