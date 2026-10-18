using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using RGBReportingWebsite.Models;

namespace RGBReportingWebsite.Services;

public sealed class PagePermissionService
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly ReportDetailsService _reportDetailsService;

    public PagePermissionService(
        AuthenticationStateProvider authenticationStateProvider,
        ReportDetailsService reportDetailsService)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _reportDetailsService = reportDetailsService;
    }

    public Task<bool> IsAdministratorAsync() => HasPermissionAsync(context => context.administrator == true);

    public Task<bool> CanScheduleReportsAsync() => HasPermissionAsync(context => context.scheduleReports == true);

    private async Task<bool> HasPermissionAsync(Func<UserContext, bool> permissionCheck)
    {
        try
        {
            var authState = await _authenticationStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            if (user.Identity?.IsAuthenticated != true)
                return false;

            var login = user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.FindFirst("preferred_username")?.Value
                ?? user.Identity.Name;
            if (string.IsNullOrWhiteSpace(login))
                return false;

            var context = await _reportDetailsService.GetUserContextAsync(login);
            return context is not null && permissionCheck(context);
        }
        catch
        {
            // Fail closed if identity or permission lookup is unavailable.
            return false;
        }
    }
}
