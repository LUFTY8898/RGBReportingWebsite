using FlexCel.Core;
using FlexCel.Render;
using FlexCel.XlsAdapter;
using Microsoft.Data.SqlClient;
using RGBReportingWebsite.Server.Models;
using System.Data;
using System.Globalization;
using System.Text;

namespace RGBReportingWebsite.Server.Services
{
    public class StandardReportService
    {
        private readonly string _connectionString;


        public StandardReportService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("Website") ?? throw new InvalidOperationException("Website database connection string not found.");
        }


        // ============================================================
        // INTERNAL COLUMN MAPPING
        //
        // results.ColumnName
        //       ==
        // reportColumnNamesDetail.columnName
        //
        // columnDisplayName is displayed in Excel.
        // ============================================================

        private sealed class ReportColumnMap
        {
            public DataColumn SourceColumn { get; init; } = null!;

            public DataRow Definition { get; init; } = null!;

            public string ColumnName { get; init; } = string.Empty;

            public string DisplayName { get; init; } = string.Empty;
        }


        // ============================================================
        // GENERATE EXCEL
        // ============================================================

        public async Task<byte[]> GenerateAsync(ReportDetail report, DataTable results)
        {
            XlsFile xls = await BuildWorkbookAsync(report, results);


            using MemoryStream stream = new MemoryStream();


            xls.Save(stream, TFileFormats.Xlsx, '\t', Encoding.UTF8);


            stream.Position = 0;


            return stream.ToArray();
        }


        // ============================================================
        // GENERATE PDF
        // ============================================================

        public async Task<byte[]> GeneratePdfAsync(ReportDetail report, DataTable results)
        {
            XlsFile xls = await BuildWorkbookAsync(report, results);

            // Landscape orientation
            xls.PrintLandscape = true;

            // Fit the report to one page wide.
            // Do not constrain the report height.
            xls.PrintToFit = true;

            xls.PrintNumberOfHorizontalPages = 1;

            xls.PrintNumberOfVerticalPages = 0;

            using MemoryStream stream = new MemoryStream();

            using (FlexCelPdfExport pdf = new FlexCelPdfExport(xls, true))
            {
                pdf.Export(stream);
            }

            stream.Position = 0;

            return stream.ToArray();
        }


        // ============================================================
        // BUILD WORKBOOK
        // ============================================================

        private async Task<XlsFile> BuildWorkbookAsync(ReportDetail report, DataTable results)
        {
            ArgumentNullException.ThrowIfNull(report);

            ArgumentNullException.ThrowIfNull(results);


            // --------------------------------------------------------
            // Load column configuration
            // --------------------------------------------------------

            DataTable reportColumnNames = await LoadReportColumnNamesAsync(report.reportDetailID);

            // --------------------------------------------------------
            // Load conditional colours
            // --------------------------------------------------------

            DataTable reportColourValues = await LoadReportColourValuesAsync(report.ExcelFile);

            // --------------------------------------------------------
            // Build explicit dataset-to-report mapping
            // --------------------------------------------------------

            List<ReportColumnMap> columns = BuildColumnMap(results, reportColumnNames);

            if (columns.Count == 0)
            {
                string datasetColumns = string.Join(", ", results.Columns.Cast<DataColumn>().Select(column => column.ColumnName));


                string reportFields = string.Join(", ", reportColumnNames.AsEnumerable().Select(row => GetString(row, "columnName")).Where(name => !string.IsNullOrWhiteSpace(name)));

                throw new InvalidOperationException(
                    "No report columns could be matched. " +
                    "The generated DataTable ColumnName must match " +
                    "reportColumnNamesDetail.columnName. " +
                    $"Dataset columns: {datasetColumns}. " +
                    $"Report column names: {reportFields}.");
            }


            // --------------------------------------------------------
            // Create workbook
            // --------------------------------------------------------

            XlsFile xls = new XlsFile(true);

            xls.NewFile(1, TExcelFileFormat.v2021);

            xls.ActiveSheet = 1;

            string worksheetName = string.IsNullOrWhiteSpace(report.WorkbookName) ? "Sheet1" : report.WorkbookName.Trim();

            xls.SheetName = MakeSafeWorksheetName(worksheetName);


            // --------------------------------------------------------
            // Dimensions
            // --------------------------------------------------------

            int noRows = results.Rows.Count;

            int noColumns = columns.Count;

            int headingRow = report.Title == true ? 3 : 1;

            int firstDataRow = headingRow + 1;


            // --------------------------------------------------------
            // Formats
            // --------------------------------------------------------

            TFlxFormat headingFormat = CreateHeadingFormat(xls);


            TFlxFormat dataFormat = CreateDataFormat(xls);


            TFlxFormat rowTotalFormat = CreateRowTotalFormat(xls);


            TFlxFormat rowTotalTitleFormat = CreateRowTotalTitleFormat(xls);


            TFlxFormat columnTotalFormat = CreateColumnTotalFormat(xls);


            // ========================================================
            // TITLE
            // ========================================================

            if (report.Title == true)
            {
                string title =
                    !string.IsNullOrWhiteSpace(
                        report.TitleName)
                        ? report.TitleName.Trim()
                        : report.Description?.Trim()
                            ?? "Report";


                xls.SetCellValue(
                    1,
                    1,
                    title);


                TFlxFormat titleFormat =
                    xls.GetCellVisibleFormatDef(
                        1,
                        1);


                titleFormat.Font.Name =
                    "Arial";


                int titleFontSize =
                    report.TitleFontSize
                        .GetValueOrDefault(36);


                if (titleFontSize <= 0)
                {
                    titleFontSize = 36;
                }


                titleFormat.Font.Size20 =
                    titleFontSize * 20;


                titleFormat.Font.Style =
                    TFlxFontStyles.Bold;


                titleFormat.Font.Underline =
                    TFlxUnderline.Single;


                titleFormat.Font.Scheme =
                    TFontScheme.None;


                titleFormat.HAlignment =
                    THFlxAlignment.center;


                titleFormat.VAlignment =
                    TVFlxAlignment.center;


                xls.SetCellFormat(
                    1,
                    1,
                    xls.AddFormat(
                        titleFormat));


                int mergeColumns =
                    noColumns;


                if (report.ColumnTotal == true)
                {
                    mergeColumns++;
                }


                if (mergeColumns > 0)
                {
                    xls.MergeCells(
                        1,
                        1,
                        1,
                        mergeColumns);
                }


                xls.AutofitRow(
                    1,
                    1,
                    false,
                    true,
                    1.0);
            }


            // ========================================================
            // HEADINGS
            // ========================================================

            for (int columnIndex = 0;
                 columnIndex < columns.Count;
                 columnIndex++)
            {
                ReportColumnMap column =
                    columns[columnIndex];


                int excelColumn =
                    columnIndex + 1;


                xls.SetCellValue(
                    headingRow,
                    excelColumn,
                    column.DisplayName);


                xls.SetCellFormat(
                    headingRow,
                    excelColumn,
                    xls.AddFormat(
                        headingFormat));
            }


            // ========================================================
            // CURRENCY COLUMN
            // ========================================================

            int currencyColumnIndex =
                FindCurrencyColumnIndex(
                    columns);


            // ========================================================
            // DATA
            // ========================================================

            for (int rowIndex = 0;
                 rowIndex < noRows;
                 rowIndex++)
            {
                int excelRow =
                    firstDataRow +
                    rowIndex;


                string currencySymbol =
                    GetCurrencySymbol(
                        results,
                        rowIndex,
                        columns,
                        currencyColumnIndex);


                for (int columnIndex = 0;
                     columnIndex < columns.Count;
                     columnIndex++)
                {
                    int excelColumn =
                        columnIndex + 1;


                    ReportColumnMap column =
                        columns[columnIndex];


                    string formatName =
                        GetString(
                            column.Definition,
                            "format");


                    object? value =
                        results.Rows[
                            rowIndex][
                            column.SourceColumn];


                    TFlxFormat cellFormat =
                        (TFlxFormat)dataFormat.Clone();


                    // ------------------------------------------------
                    // The Excel Table supplies banded rows.
                    // We no longer manually paint alternating rows.
                    // ------------------------------------------------

                    ApplyNumberFormat(
                        cellFormat,
                        formatName,
                        currencySymbol);


                    SetCellValue(
                        xls,
                        excelRow,
                        excelColumn,
                        value,
                        formatName);


                    ApplyConditionalColour(
                        cellFormat,
                        reportColourValues,
                        column.ColumnName,
                        value);


                    xls.SetCellFormat(
                        excelRow,
                        excelColumn,
                        xls.AddFormat(
                            cellFormat));
                }
            }


            // ========================================================
            // EXCEL TABLE
            //
            // Header row + actual data rows only.
            //
            // Title and custom total rows remain outside the table.
            // ========================================================

            if (noColumns > 0 &&
                noRows > 0)
            {
                AddExcelTable(
                    xls,
                    report,
                    headingRow,
                    noColumns,
                    noRows);
            }


            // ========================================================
            // COLUMN TOTAL
            // ========================================================

            int noColumnsWithTotal =
                noColumns;


            if (report.ColumnTotal == true &&
                noRows > 0)
            {
                noColumnsWithTotal++;


                AddColumnTotal(
                    xls,
                    report,
                    noRows,
                    noColumns,
                    firstDataRow,
                    columnTotalFormat);
            }


            // ========================================================
            // ROW TOTAL
            // ========================================================

            if (report.RowTotal == true &&
                noRows > 0)
            {
                AddRowTotal(
                    xls,
                    report,
                    columns,
                    noRows,
                    noColumnsWithTotal,
                    firstDataRow,
                    rowTotalFormat,
                    rowTotalTitleFormat);
            }


            // ========================================================
            // HEADING HEIGHT
            // ========================================================

            if (report.HeadingHeight
                    .GetValueOrDefault() > 0)
            {
                xls.SetRowHeight(
                    headingRow,
                    report.HeadingHeight!
                        .Value *
                    20);
            }


            // ========================================================
            // COLUMN WIDTHS
            // ========================================================

            ApplyColumnWidths(
                xls,
                columns);


            // ========================================================
            // FREEZE PANES
            //
            // AddTable already provides filter buttons, so we don't
            // call SetAutoFilter separately.
            // ========================================================

            xls.FreezePanes(
                new TCellAddress(
                    "A" +
                    firstDataRow.ToString(
                        CultureInfo.InvariantCulture)));


            // ========================================================
            // PRINT OPTIONS
            // ========================================================

            xls.PrintLandscape =
                true;


            xls.PrintToFit =
                true;


            return xls;
        }


        // ============================================================
        // ADD GENUINE EXCEL TABLE
        //
        // FlexCel/TMS supports AddTable using TTableDefinition.
        // TableStyleMedium2 supplies the native table styling and
        // alternating/banded rows.
        // ============================================================

        private static void AddExcelTable(
            XlsFile xls,
            ReportDetail report,
            int headingRow,
            int noColumns,
            int noRows)
        {
            int bottomRow =
                headingRow +
                noRows;


            string tableName =
                GetSafeTableName(
                    report,
                    xls.ActiveSheet);


            TTableDefinition table =
                new TTableDefinition
                {
                    Name =
                        tableName,

                    HasAutofilter =
                        true,

                    HasHeaderRow =
                        true,

                    Style =
                        new TTableStyle(
                            "TableStyleMedium2",
                            false,
                            true,
                            false,
                            true),

                    Range =
                        new TXlsCellRange(
                            headingRow,
                            1,
                            bottomRow,
                            noColumns)
                };


            xls.AddTable(
                table);
        }


        // ============================================================
        // BUILD COLUMN MAP
        //
        // Matching:
        //
        // results.ColumnName
        //       ==
        // reportColumnNamesDetail.columnName
        //
        // The order of reportColumnNamesDetail controls Excel output
        // order.
        // ============================================================

        private static List<ReportColumnMap> BuildColumnMap(
            DataTable results,
            DataTable reportColumnNames)
        {
            List<ReportColumnMap> mappings =
                new List<ReportColumnMap>();


            foreach (DataRow definition in
                     reportColumnNames.Rows)
            {
                bool includeField =
                    GetBool(
                        definition,
                        "includeField",
                        true);


                if (!includeField)
                {
                    continue;
                }


                string columnName =
                    GetString(
                        definition,
                        "columnName");


                if (string.IsNullOrWhiteSpace(
                        columnName))
                {
                    continue;
                }


                DataColumn? sourceColumn =
                    results.Columns
                        .Cast<DataColumn>()
                        .FirstOrDefault(column =>
                            string.Equals(
                                column.ColumnName.Trim(),
                                columnName.Trim(),
                                StringComparison.OrdinalIgnoreCase));


                if (sourceColumn == null)
                {
                    continue;
                }


                string displayName =
                    GetString(
                        definition,
                        "columnDisplayName");


                if (string.IsNullOrWhiteSpace(
                        displayName))
                {
                    displayName =
                        columnName;
                }


                mappings.Add(
                    new ReportColumnMap
                    {
                        SourceColumn =
                            sourceColumn,

                        Definition =
                            definition,

                        ColumnName =
                            columnName,

                        DisplayName =
                            displayName
                    });
            }


            return mappings;
        }


        // ============================================================
        // LOAD REPORT COLUMN DEFINITIONS
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
                    _connectionString);


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
        // LOAD CONDITIONAL COLOURS
        // ============================================================

        private async Task<DataTable> LoadReportColourValuesAsync(
            string? excelFile)
        {
            DataTable table =
                new DataTable(
                    "reportColourValues");


            if (string.IsNullOrWhiteSpace(
                    excelFile))
            {
                return table;
            }


            const string sql = @"
                SELECT *
                FROM dbo.reportColourValues
                WHERE excelFile = @ExcelFile
                ORDER BY value;";


            await using SqlConnection connection =
                new SqlConnection(
                    _connectionString);


            await connection.OpenAsync();


            await using SqlCommand command =
                new SqlCommand(
                    sql,
                    connection);


            command.CommandTimeout =
                3600;


            command.Parameters.Add(
                "@ExcelFile",
                SqlDbType.NVarChar,
                100)
                .Value =
                excelFile.Trim();


            await using SqlDataReader reader =
                await command
                    .ExecuteReaderAsync();


            table.Load(
                reader);


            return table;
        }


        // ============================================================
        // FIND CURRENCY COLUMN
        // ============================================================

        private static int FindCurrencyColumnIndex(
            List<ReportColumnMap> columns)
        {
            for (int index = 0;
                 index < columns.Count;
                 index++)
            {
                if (GetBool(
                        columns[index]
                            .Definition,
                        "currency"))
                {
                    return index;
                }
            }


            return -1;
        }


        // ============================================================
        // CURRENCY SYMBOL
        // ============================================================

        private static string GetCurrencySymbol(
            DataTable results,
            int rowIndex,
            List<ReportColumnMap> columns,
            int currencyColumnIndex)
        {
            if (currencyColumnIndex < 0 ||
                currencyColumnIndex >=
                    columns.Count)
            {
                return "£";
            }


            ReportColumnMap currencyColumn =
                columns[
                    currencyColumnIndex];


            object? value =
                results.Rows[
                    rowIndex][
                    currencyColumn.SourceColumn];


            string currency =
                Convert.ToString(
                    value)
                ?.Trim()
                ?? string.Empty;


            return currency
                .ToUpperInvariant() switch
            {
                "EUR" => "€",
                "USD" => "$",
                "GBP" => "£",
                _ => "£"
            };
        }


        // ============================================================
        // SET CELL VALUE
        // ============================================================

        private static void SetCellValue(
            XlsFile xls,
            int row,
            int column,
            object? value,
            string formatName)
        {
            if (value == null ||
                value == DBNull.Value)
            {
                xls.SetCellValue(
                    row,
                    column,
                    null);

                return;
            }


            if (formatName.Equals(
                    "Text",
                    StringComparison.OrdinalIgnoreCase))
            {
                xls.SetCellValue(
                    row,
                    column,
                    Convert
                        .ToString(value)
                        ?.Trim());

                return;
            }


            if (formatName.Equals(
                    "Time",
                    StringComparison.OrdinalIgnoreCase) ||
                formatName.Equals(
                    "T2",
                    StringComparison.OrdinalIgnoreCase) ||
                formatName.Equals(
                    "T3",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetTime(
                        value,
                        out TimeSpan time))
                {
                    DateTime excelTime =
                        new DateTime(
                            1899,
                            12,
                            30)
                        .Add(time);


                    xls.SetCellValue(
                        row,
                        column,
                        FlxDateTime.ToOADate(
                            excelTime,
                            xls.OptionsDates1904));


                    return;
                }
            }


            xls.SetCellValue(
                row,
                column,
                value);
        }


        // ============================================================
        // NUMBER FORMAT
        // ============================================================

        private static void ApplyNumberFormat(
            TFlxFormat format,
            string formatName,
            string currencySymbol)
        {
            switch (formatName.Trim())
            {
                case "Text":

                    format.Format =
                        "@";

                    break;


                case "Date":

                    format.Format =
                        "dd/mm/yyyy";

                    break;


                case "DateTime":

                    format.Format =
                        "dd/mm/yyyy hh:mm:ss";

                    break;


                case "C0":
                case "E0":

                    format.Format =
                        currencySymbol +
                        "#,##0";

                    break;


                case "C2":
                case "E2":

                    format.Format =
                        currencySymbol +
                        "#,##0.00";

                    break;


                case "C4":
                case "E4":

                    format.Format =
                        currencySymbol +
                        "#,##0.0000";

                    break;


                case "C2R":
                case "E2R":

                    format.Format =
                        currencySymbol +
                        "#,##0.00;[Red]-" +
                        currencySymbol +
                        "#,##0.00";

                    break;


                case "N0":

                    format.Format =
                        "#,##0";

                    break;


                case "N1":

                    format.Format =
                        "#,##0.0";

                    break;


                case "N2":

                    format.Format =
                        "#,##0.00";

                    break;


                case "N4":

                    format.Format =
                        "#,##0.0000";

                    break;


                case "P2":

                    format.Format =
                        "0.00%";

                    break;


                case "P2R":

                    format.Format =
                        "0.00%;[Red]-0.00%";

                    break;


                case "Time":
                case "T2":

                    format.Format =
                        "[hh\\]:mm";

                    break;


                case "T3":

                    format.Format =
                        "[hh\\]:mm:ss";

                    break;
            }
        }


        // ============================================================
        // CONDITIONAL COLOUR
        // ============================================================

        private static void ApplyConditionalColour(
            TFlxFormat format,
            DataTable colourValues,
            string columnName,
            object? value)
        {
            if (value == null ||
                value == DBNull.Value)
            {
                return;
            }


            if (!decimal.TryParse(
                    Convert.ToString(value),
                    out decimal compareValue))
            {
                return;
            }


            DataRow? selectedColour =
                null;


            foreach (DataRow colourRow in
                     colourValues.Rows)
            {
                string colourColumn =
                    GetString(
                        colourRow,
                        "columnName");


                if (!colourColumn.Equals(
                        columnName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }


                decimal threshold =
                    GetDecimal(
                        colourRow,
                        "value");


                // Threshold is inclusive.
                if (compareValue >= threshold)
                {
                    selectedColour =
                        colourRow;
                }
            }


            if (selectedColour == null)
            {
                return;
            }


            byte red =
                Convert.ToByte(
                    selectedColour["red"]);


            byte green =
                Convert.ToByte(
                    selectedColour["green"]);


            byte blue =
                Convert.ToByte(
                    selectedColour["blue"]);


            format.FillPattern.FgColor =
                TExcelColor.FromArgb(
                    red,
                    green,
                    blue);
        }


        // ============================================================
        // COLUMN TOTAL
        // ============================================================

        private static void AddColumnTotal(
            XlsFile xls,
            ReportDetail report,
            int noRows,
            int noColumns,
            int firstDataRow,
            TFlxFormat totalFormat)
        {
            int totalColumn =
                noColumns + 1;


            int startColumn =
                report.ColumnTotalStart
                    .GetValueOrDefault(1);


            if (startColumn < 1)
            {
                startColumn = 1;
            }


            int endPadding =
                report.ColumnTotalEnd
                    .GetValueOrDefault(0);


            int endColumn =
                noColumns -
                endPadding;


            if (endColumn < startColumn)
            {
                return;
            }


            string totalType =
                string.IsNullOrWhiteSpace(
                    report.ColumnTotalType)
                    ? "SUM"
                    : report.ColumnTotalType.Trim();


            string subtotalPrefix =
                string.Empty;


            if (totalType.Equals(
                    "Subtotal",
                    StringComparison.OrdinalIgnoreCase))
            {
                totalType =
                    "SUBTOTAL";

                subtotalPrefix =
                    "9,";
            }


            string fromColumn =
                ExcelColumnNumberToLetter(
                    startColumn);


            string toColumn =
                ExcelColumnNumberToLetter(
                    endColumn);


            for (int rowIndex = 0;
                 rowIndex < noRows;
                 rowIndex++)
            {
                int excelRow =
                    firstDataRow +
                    rowIndex;


                string formula =
                    "=" +
                    totalType +
                    "(" +
                    subtotalPrefix +
                    fromColumn +
                    excelRow +
                    ":" +
                    toColumn +
                    excelRow +
                    ")";


                xls.SetCellValue(
                    excelRow,
                    totalColumn,
                    new TFormula(
                        formula));


                TFlxFormat currentFormat =
                    (TFlxFormat)totalFormat
                        .Clone();


                ApplyNumberFormat(
                    currentFormat,
                    report.ColumnTotalFormat
                        ?? string.Empty,
                    "£");


                xls.SetCellFormat(
                    excelRow,
                    totalColumn,
                    xls.AddFormat(
                        currentFormat));
            }


            int headingRow =
                firstDataRow -
                1;


            xls.SetCellValue(
                headingRow,
                totalColumn,
                report.ColumnTotalTitle?.Trim()
                    ?? "Total");


            TFlxFormat headingFormat =
                CreateHeadingFormat(
                    xls);


            xls.SetCellFormat(
                headingRow,
                totalColumn,
                xls.AddFormat(
                    headingFormat));
        }


        // ============================================================
        // ROW TOTAL
        // ============================================================

        private static void AddRowTotal(
            XlsFile xls,
            ReportDetail report,
            List<ReportColumnMap> columns,
            int noRows,
            int noColumns,
            int firstDataRow,
            TFlxFormat totalFormat,
            TFlxFormat titleFormat)
        {
            int totalRow =
                firstDataRow +
                noRows;


            int startColumn =
                report.RowTotalStart
                    .GetValueOrDefault(1);


            if (startColumn < 1)
            {
                startColumn = 1;
            }


            int endPadding =
                report.RowTotalEnd
                    .GetValueOrDefault(0);


            int endColumn =
                noColumns -
                endPadding;


            if (endColumn < startColumn)
            {
                return;
            }


            if (report.RowTotalHeight
                    .GetValueOrDefault() > 0)
            {
                xls.SetRowHeight(
                    totalRow,
                    Convert.ToInt32(
                        Math.Round(
                            report.RowTotalHeight!
                                .Value *
                            20)));
            }


            if (startColumn > 1)
            {
                xls.SetCellValue(
                    totalRow,
                    startColumn - 1,
                    report.RowTotalTitle?.Trim()
                        ?? "Total");


                xls.SetCellFormat(
                    totalRow,
                    startColumn - 1,
                    xls.AddFormat(
                        titleFormat));
            }


            string totalType =
                string.IsNullOrWhiteSpace(
                    report.RowTotalType)
                    ? "SUM"
                    : report.RowTotalType.Trim();


            string subtotalPrefix =
                string.Empty;


            if (totalType.Equals(
                    "Subtotal",
                    StringComparison.OrdinalIgnoreCase))
            {
                totalType =
                    "SUBTOTAL";

                subtotalPrefix =
                    "9,";
            }


            for (int excelColumn =
                    startColumn;
                 excelColumn <= endColumn;
                 excelColumn++)
            {
                string columnLetter =
                    ExcelColumnNumberToLetter(
                        excelColumn);


                string formula =
                    "=" +
                    totalType +
                    "(" +
                    subtotalPrefix +
                    columnLetter +
                    firstDataRow +
                    ":" +
                    columnLetter +
                    (firstDataRow +
                     noRows -
                     1) +
                    ")";


                xls.SetCellValue(
                    totalRow,
                    excelColumn,
                    new TFormula(
                        formula));


                TFlxFormat currentFormat =
                    (TFlxFormat)totalFormat
                        .Clone();


                int mappedIndex =
                    excelColumn -
                    1;


                if (mappedIndex >= 0 &&
                    mappedIndex < columns.Count)
                {
                    string formatName =
                        GetString(
                            columns[
                                mappedIndex]
                                .Definition,
                            "format");


                    ApplyNumberFormat(
                        currentFormat,
                        formatName,
                        "£");
                }


                xls.SetCellFormat(
                    totalRow,
                    excelColumn,
                    xls.AddFormat(
                        currentFormat));
            }
        }


        // ============================================================
        // COLUMN WIDTHS
        // ============================================================

        private static void ApplyColumnWidths(
            XlsFile xls,
            List<ReportColumnMap> columns)
        {
            for (int index = 0;
                 index < columns.Count;
                 index++)
            {
                int excelColumn =
                    index +
                    1;


                DataRow definition =
                    columns[
                        index]
                        .Definition;


                xls.AutofitCol(
                    excelColumn,
                    excelColumn,
                    false,
                    1.1);


                int width =
                    xls.GetColWidth(
                        excelColumn);


                decimal minimum =
                    GetDecimal(
                        definition,
                        "columnWidthMinimum");


                decimal maximum =
                    GetDecimal(
                        definition,
                        "columnWidthMaximum");


                int minimumWidth =
                    Convert.ToInt32(
                        Math.Round(
                            minimum *
                            273));


                int maximumWidth =
                    Convert.ToInt32(
                        Math.Round(
                            maximum *
                            273));


                if (maximumWidth > 0 &&
                    width > maximumWidth)
                {
                    width =
                        maximumWidth;
                }


                if (minimumWidth > 0 &&
                    width < minimumWidth)
                {
                    width =
                        minimumWidth;
                }


                xls.SetColWidth(
                    excelColumn,
                    width);
            }
        }


        // ============================================================
        // HEADING FORMAT
        // ============================================================

        private static TFlxFormat CreateHeadingFormat(
            XlsFile xls)
        {
            TFlxFormat format =
                xls.GetCellVisibleFormatDef(
                    1,
                    1);


            format.Font.Name =
                "Arial";


            format.Font.Size20 =
                200;


            format.Font.Color =
                TExcelColor.FromIndex(2);


            format.Font.Style =
                TFlxFontStyles.Bold;


            format.Font.Scheme =
                TFontScheme.None;


            format.Borders.Left.Style =
                TFlxBorderStyle.Medium;


            format.Borders.Left.Color =
                TExcelColor.Automatic;


            format.Borders.Right.Style =
                TFlxBorderStyle.Medium;


            format.Borders.Right.Color =
                TExcelColor.Automatic;


            format.Borders.Top.Style =
                TFlxBorderStyle.Medium;


            format.Borders.Top.Color =
                TExcelColor.Automatic;


            format.Borders.Bottom.Style =
                TFlxBorderStyle.Medium;


            format.Borders.Bottom.Color =
                TExcelColor.Automatic;


            format.FillPattern.Pattern =
                TFlxPatternStyle.Solid;


            format.FillPattern.FgColor =
                TExcelColor.FromTheme(
                    TThemeColor.Accent1);


            format.FillPattern.BgColor =
                TExcelColor.Automatic;


            format.HAlignment =
                THFlxAlignment.center;


            format.VAlignment =
                TVFlxAlignment.center;


            format.WrapText =
                true;


            return format;
        }


        // ============================================================
        // DATA FORMAT
        // ============================================================

        private static TFlxFormat CreateDataFormat(
            XlsFile xls)
        {
            TFlxFormat format =
                xls.GetCellVisibleFormatDef(
                    1,
                    1);


            format.Font.Name =
                "Arial";


            format.Font.Size20 =
                200;


            format.Font.Scheme =
                TFontScheme.None;


            format.Borders.Left.Style =
                TFlxBorderStyle.Thin;


            format.Borders.Left.Color =
                TExcelColor.Automatic;


            format.Borders.Right.Style =
                TFlxBorderStyle.Thin;


            format.Borders.Right.Color =
                TExcelColor.Automatic;


            format.Borders.Top.Style =
                TFlxBorderStyle.Thin;


            format.Borders.Top.Color =
                TExcelColor.Automatic;


            format.Borders.Bottom.Style =
                TFlxBorderStyle.Thin;


            format.Borders.Bottom.Color =
                TExcelColor.Automatic;


            format.VAlignment =
                TVFlxAlignment.center;


            format.WrapText =
                true;


            return format;
        }


        // ============================================================
        // ROW TOTAL FORMAT
        // ============================================================

        private static TFlxFormat CreateRowTotalFormat(
            XlsFile xls)
        {
            TFlxFormat format =
                CreateDataFormat(
                    xls);


            format.Font.Size20 =
                220;


            format.Font.Style =
                TFlxFontStyles.Bold;


            format.Font.Color =
                TExcelColor.FromIndex(2);


            format.FillPattern.Pattern =
                TFlxPatternStyle.Solid;


            format.FillPattern.FgColor =
                TExcelColor.FromTheme(
                    TThemeColor.Accent1);


            return format;
        }


        // ============================================================
        // ROW TOTAL TITLE FORMAT
        // ============================================================

        private static TFlxFormat CreateRowTotalTitleFormat(
            XlsFile xls)
        {
            TFlxFormat format =
                xls.GetCellVisibleFormatDef(
                    1,
                    1);


            format.Font.Name =
                "Arial";


            format.Font.Size20 =
                260;


            format.Font.Style =
                TFlxFontStyles.Bold;


            format.HAlignment =
                THFlxAlignment.right;


            format.VAlignment =
                TVFlxAlignment.center;


            format.WrapText =
                false;


            return format;
        }


        // ============================================================
        // COLUMN TOTAL FORMAT
        // ============================================================

        private static TFlxFormat CreateColumnTotalFormat(
            XlsFile xls)
        {
            TFlxFormat format =
                CreateRowTotalFormat(
                    xls);


            format.Font.Style =
                TFlxFontStyles.None;


            return format;
        }


        // ============================================================
        // TIME
        // ============================================================

        private static bool TryGetTime(
            object value,
            out TimeSpan time)
        {
            if (value is TimeSpan timeSpan)
            {
                time =
                    timeSpan;


                return true;
            }


            if (value is DateTime dateTime)
            {
                time =
                    dateTime.TimeOfDay;


                return true;
            }


            return TimeSpan.TryParse(
                Convert.ToString(
                    value),
                out time);
        }


        // ============================================================
        // SAFE WORKSHEET NAME
        // ============================================================

        private static string MakeSafeWorksheetName(
            string name)
        {
            char[] invalidCharacters =
            {
                ':',
                '\\',
                '/',
                '?',
                '*',
                '[',
                ']'
            };


            foreach (char character in
                     invalidCharacters)
            {
                name =
                    name.Replace(
                        character,
                        '_');
            }


            if (name.Length > 31)
            {
                name =
                    name.Substring(
                        0,
                        31);
            }


            if (string.IsNullOrWhiteSpace(
                    name))
            {
                name =
                    "Sheet1";
            }


            return name;
        }


        // ============================================================
        // SAFE TABLE NAME
        //
        // Excel table names can't contain spaces and should not look
        // like a cell reference, so prefix with ReportTable_.
        // ============================================================

        private static string GetSafeTableName(
            ReportDetail report,
            int sheetNumber)
        {
            string baseName =
                !string.IsNullOrWhiteSpace(
                    report.SpreadsheetName)
                    ? report.SpreadsheetName.Trim()
                    : "Report";


            StringBuilder safeName =
                new StringBuilder();


            foreach (char character in
                     baseName)
            {
                if (char.IsLetterOrDigit(
                        character) ||
                    character == '_')
                {
                    safeName.Append(
                        character);
                }
                else
                {
                    safeName.Append(
                        '_');
                }
            }


            string result =
                safeName
                    .ToString()
                    .Trim('_');


            if (string.IsNullOrWhiteSpace(
                    result))
            {
                result =
                    "Report";
            }


            return
                $"ReportTable_{report.reportDetailID}_{sheetNumber}_{result}";
        }


        // ============================================================
        // EXCEL COLUMN LETTER
        // ============================================================

        private static string ExcelColumnNumberToLetter(
            int columnNumber)
        {
            string columnName =
                string.Empty;


            while (columnNumber > 0)
            {
                int modulo =
                    (columnNumber -
                     1) %
                    26;


                columnName =
                    Convert.ToChar(
                        'A' +
                        modulo) +
                    columnName;


                columnNumber =
                    (columnNumber -
                     modulo) /
                    26;
            }


            return columnName;
        }


        // ============================================================
        // DATAROW STRING
        // ============================================================

        private static string GetString(
            DataRow? row,
            string columnName)
        {
            if (row == null)
            {
                return string.Empty;
            }


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
        // DATAROW BOOL
        // ============================================================

        private static bool GetBool(
            DataRow? row,
            string columnName,
            bool defaultValue = false)
        {
            if (row == null)
            {
                return defaultValue;
            }


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


        // ============================================================
        // DATAROW DECIMAL
        // ============================================================

        private static decimal GetDecimal(
            DataRow? row,
            string columnName,
            decimal defaultValue = 0)
        {
            if (row == null)
            {
                return defaultValue;
            }


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


            if (decimal.TryParse(
                    Convert.ToString(
                        row[columnName]),
                    out decimal result))
            {
                return result;
            }


            return defaultValue;
        }
    }
}