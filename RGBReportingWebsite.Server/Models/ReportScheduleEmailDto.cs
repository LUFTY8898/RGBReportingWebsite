namespace RGBReportingWebsite.Server.Models;

public sealed class ReportScheduleEmailDto
{
    public int reportScheduleEmailID { get; set; }
    public int? scheduledReportID { get; set; }
    public int? userID { get; set; }
    public string? type { get; set; }
    public string? frequency { get; set; }
    public TimeSpan? runTime { get; set; }
    public bool? active { get; set; }
    public string? fullName { get; set; }
    public string? loginID { get; set; }
}
