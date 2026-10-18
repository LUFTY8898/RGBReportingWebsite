namespace RGBReportingWebsite.Models
{
    public class UserContext
    {
        public int? userID { get; set; }
        public int? SAPuserID { get; set; }
        public int? SAPsalesID { get; set; }
        public string? fullName { get; set; }
        public bool? runReports { get; set; }
        public bool? scheduleReports { get; set; }
        public bool? manageReports { get; set; }
        public bool? administrator { get; set; }
    }
}
