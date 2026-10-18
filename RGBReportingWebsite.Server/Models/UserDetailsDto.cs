namespace RGBReportingWebsite.Server.Models
{
    public class UserDetailsDto
    {
        public int userID { get; set; }
        public string? loginID { get; set; }
        public string? fullName { get; set; }
        public int? SAPuserID { get; set; }
        public int? SAPsalesID { get; set; }
        public bool active { get; set; }

        public bool runReports { get; set; }
        public bool scheduleReports { get; set; }
        public bool manageReports { get; set; }
        public bool administrator { get; set; }
    }
}
