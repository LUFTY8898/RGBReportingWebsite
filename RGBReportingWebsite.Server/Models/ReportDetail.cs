namespace RGBReportingWebsite.Server.Models
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


        // ------------------------------------------------------------
        // manufacturerSelect contains the SQL field/expression
        // that should be filtered using the selected OMRC FirmCode.
        //
        // Example:
        //
        // T0.FirmCode
        //
        // Null / blank means no manufacturer selector.
        // ------------------------------------------------------------

        public string? manufacturerSelect { get; set; }


        // ------------------------------------------------------------
        // dateRangeSelect determines whether RunReport.razor displays
        // From Date and To Date controls.
        // ------------------------------------------------------------

        public string? dateRangeSelect { get; set; }

        // ------------------------------------------------------------
        // accountManagerSelect determines whether RunReport.razor displays
        // from the logged in user.
        // ------------------------------------------------------------

        public string? accountManagerSelect { get; set; }

        // ------------------------------------------------------------
        // periodSelect removed from reportDetails
        // ------------------------------------------------------------

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
        public int? SAPsalesID { get; set; }

    }
}