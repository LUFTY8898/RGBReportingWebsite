namespace RGBReportingWebsite.Models;

public sealed class ReportScheduleHistoryRecord
{
    public int reportScheduleHistoryID { get; set; }
    public DateTime? reportScheduleHistoryDate { get; set; }
    public int? scheduledReportID { get; set; }
    public string? toEmailAddress { get; set; }
    public string? ccEmailAddress { get; set; }
    public string? BccEmailAddress { get; set; }
    public bool? successful { get; set; }
    public string? errorMessage { get; set; }
}
