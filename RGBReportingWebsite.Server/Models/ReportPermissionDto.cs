namespace RGBReportingWebsite.Server.Models
{
    public class ReportPermissionDto
    {
        public int reportDetailID { get; set; }
        public string? category { get; set; }
        public string? subCategory { get; set; }
        public string? description { get; set; }
        public bool reportEnabled { get; set; }
        public bool permissionEnabled { get; set; }
    }
}
