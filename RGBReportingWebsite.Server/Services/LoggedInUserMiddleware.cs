using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace RGBReportingWebsite.Server.Services
{
    public class LoggedInUserMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggedInUserMiddleware> _logger;

        public LoggedInUserMiddleware(RequestDelegate next, ILogger<LoggedInUserMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                var accessor = context.RequestServices.GetService(typeof(LoggedInUserAccessor)) as LoggedInUserAccessor;
                var reportDetailsService = context.RequestServices.GetService(typeof(ReportDetailsDatabaseService)) as ReportDetailsDatabaseService;

                if (accessor != null && reportDetailsService != null)
                {
                    string? login = null;

                    // Prefer Email claim, then preferred_username, then Name
                    login = context.User?.FindFirst(ClaimTypes.Email)?.Value;
                    if (string.IsNullOrWhiteSpace(login))
                        login = context.User?.FindFirst("preferred_username")?.Value;
                    if (string.IsNullOrWhiteSpace(login))
                        login = context.User?.Identity?.Name;

                    if (!string.IsNullOrWhiteSpace(login))
                    {
                        var ctx = await reportDetailsService.GetUserContextByLoginAsync(login);
                        if (ctx != null)
                        {
                            accessor.LoggedInUserID = ctx.userID;
                            accessor.Login = login;
                            accessor.FullName = ctx.fullName;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to resolve logged-in user context in middleware.");
            }

            await _next(context);
        }
    }
}
