namespace RGBReportingWebsite.Models
{
    public class ReportPermission
    {
        public int reportDetailID { get; set; }
        public string? category { get; set; }
        public string? subCategory { get; set; }
        public string? description { get; set; }
        public bool reportEnabled { get; set; }
        public bool permissionEnabled { get; set; }
    }
}
