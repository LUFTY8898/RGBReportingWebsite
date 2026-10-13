namespace RGBReportingWebsite.Services
{
    public class ReportDetail
    {
        // ============================================================
        // REPORT IDENTITY
        // ============================================================

        public int reportDetailID { get; set; }

        public string ExcelFile { get; set; } = string.Empty;

        public bool? Enabled { get; set; }


        // ============================================================
        // OUTPUT GENERATION
        // ============================================================

        public bool? ExcelGeneration { get; set; }

        public bool? PdfGeneration { get; set; }


        // ============================================================
        // REPORT MENU / DESCRIPTION
        // ============================================================

        public string? Category { get; set; }

        public string? SubCategory { get; set; }

        public string? Description { get; set; }


        // ============================================================
        // QUERY / DATASET
        // ============================================================

        public string? QueryType { get; set; }

        public string? QueryName { get; set; }


        // ============================================================
        // OUTPUT FILE
        // ============================================================

        public string? SavePath { get; set; }

        public string? SpreadsheetName { get; set; }

        public string? WorkbookName { get; set; }

        public string? FilePath { get; set; }


        // ============================================================
        // DYNAMIC FIELDS
        // ============================================================

        public bool? DynamicFields { get; set; }

        public string? DefaultFieldType { get; set; }

        public decimal? DefaultColumnWidthMinimum { get; set; }

        public decimal? DefaultColumnWidthMaximum { get; set; }


        // ============================================================
        // REPORT FILTER / SELECTION OPTIONS
        // ============================================================

        // clientSelect, branchFilter, areaSelect and nominalSelect removed from reportDetails
        // dateRangeSelect is stored as SQL expression (nvarchar)
        public string? dateRangeSelect { get; set; }

        // Helper: true when the report is configured to require a date range
        public bool RequiresDateRange()
        {
            return !string.IsNullOrWhiteSpace(this.dateRangeSelect);
        }
     
        // ------------------------------------------------------------
        // Contains the configured SQL field used for the
        // manufacturer filter.
        //
        // Example:
        // T0.FirmCode
        //
        // Null / blank means the Manufacturer dropdown is not shown.
        // ------------------------------------------------------------

        public string? manufacturerSelect { get; set; }


        // ------------------------------------------------------------
        // True = show From Date / To Date on RunReport.razor.
        // ------------------------------------------------------------

        // PeriodSelect removed from reportDetails

        // ------------------------------------------------------------
        // accountManagerSelect determines whether RunReport.razor displays
        // an Account Manager selector populated from the server.
        // ------------------------------------------------------------

        public string? accountManagerSelect { get; set; }


        // ============================================================
        // HEADING
        // ============================================================

        public int? HeadingHeight { get; set; }


        // ============================================================
        // REPORT TITLE
        // ============================================================

        public bool? Title { get; set; }

        public string? TitleName { get; set; }

        public int? TitleFontSize { get; set; }


        // ============================================================
        // COLUMN TOTAL
        // ============================================================

        public bool? ColumnTotal { get; set; }

        public string? ColumnTotalType { get; set; }

        public string? ColumnTotalFormat { get; set; }

        public decimal? ColumnTotalWidth { get; set; }

        public string? ColumnTotalTitle { get; set; }

        public int? ColumnTotalStart { get; set; }

        public int? ColumnTotalEnd { get; set; }


        // ============================================================
        // ROW TOTAL
        // ============================================================

        public bool? RowTotal { get; set; }
        public string? RowTotalType { get; set; }
        public string? RowTotalFormat { get; set; }
        public decimal? RowTotalHeight { get; set; }
        public string? RowTotalTitle { get; set; }
        public int? RowTotalStart { get; set; }
        public int? RowTotalEnd { get; set; }


        // ============================================================
        // PERMITTED USER
        // ============================================================

        public int? userID { get; set; }
    }
}