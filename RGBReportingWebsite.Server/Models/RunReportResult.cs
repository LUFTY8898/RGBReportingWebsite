namespace RGBReportingWebsite.Server.Models
{
    public class RunReportResult
    {
        public int reportDetailID { get; set; }

        public string? Description { get; set; }

        public string? Category { get; set; }

        public string? SubCategory { get; set; }

        public string? QueryType { get; set; }

        public string? QueryName { get; set; }

        public List<string> Columns { get; set; } = new();

        public List<Dictionary<string, object?>> Rows { get; set; } = new();
    }
}