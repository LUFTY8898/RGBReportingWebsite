using System;

namespace RGBReportingWebsite.Server.Services
{
    // Scoped accessor to store resolved numeric user id for the current request.
    public class LoggedInUserAccessor
    {
        // Numeric user id resolved from reportUsers.loginID
        public int? LoggedInUserID { get; set; }

        // Original login string used for resolution (email or preferred_username)
        public string? Login { get; set; }

        // Optional full name if resolved
        public string? FullName { get; set; }
    }
}
