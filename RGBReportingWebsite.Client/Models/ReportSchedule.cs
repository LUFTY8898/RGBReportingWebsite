namespace RGBReportingWebsite.Models;

public sealed class ReportSchedule
{
    public int scheduledReportID { get; set; }
    public int? reportRunOrder { get; set; }
    public int? reportGroupingID { get; set; }
    public string? reportName { get; set; }
    public string? spreadsheetName { get; set; }
    public string? parameter1 { get; set; }
    public string? parameter1Name { get; set; }
    public string? filterCommand { get; set; }
    public string? filterCommandName { get; set; }
    public bool? enabled { get; set; }
}
