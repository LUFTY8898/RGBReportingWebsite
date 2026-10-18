using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using RGBReportingWebsite;
using RGBReportingWebsite.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient to use the server API base address when running in development.
// By default, HostEnvironment.BaseAddress points to the client origin which may not
// be the server API host/port used by Visual Studio. Use NavigationManager-based
// fallback inside services when necessary.
builder.Services.AddScoped(sp =>
    new HttpClient
    {
        BaseAddress = builder.HostEnvironment.IsDevelopment()
            ? new Uri("https://localhost:7278/")
            : new Uri(builder.HostEnvironment.BaseAddress)
    });

builder.Services.AddScoped<ReportDetailsService>();
builder.Services.AddScoped<PagePermissionService>();

builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind(
        "AzureAd",
        options.ProviderOptions.Authentication);
});

await builder.Build().RunAsync();