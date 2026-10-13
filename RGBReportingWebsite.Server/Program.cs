using Microsoft.Data.SqlClient;
using RGBReportingWebsite.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure the server listens on the development ports the client expects when
// running under Visual Studio. Provide both HTTP (7278) and HTTPS (7279).
builder.WebHost.UseUrls("https://localhost:7278");

// In Production use Integrated Security.
// In Development use the connection strings supplied by User Secrets.
if (!builder.Environment.IsDevelopment())
{
    ConfigureIntegratedSecurity(builder, "RGB_Reporting");
    ConfigureIntegratedSecurity(builder, "Website");
}

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddControllers();

builder.Services.AddAuthorization();

builder.Services.AddScoped<ReportDetailsDatabaseService>();
builder.Services.AddScoped<DatasetService>();
builder.Services.AddScoped<StandardReportService>();
builder.Services.AddScoped<ManufacturerService>();
// Scoped accessor to hold resolved numeric user id for the current request
builder.Services.AddScoped<LoggedInUserAccessor>();
// Register middleware and logging DI


builder.Services.AddCors(options =>
{
    options.AddPolicy("RGBReportingClient", policy =>
    {
        policy
            .WithOrigins("https://localhost:7231")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Insert middleware to populate LoggedInUserAccessor for each request
app.UseMiddleware<LoggedInUserMiddleware>();

// Log active environment and connection strings
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider
        .GetRequiredService<ILogger<Program>>();

    logger.LogInformation(
        "Environment: {Environment}",
        app.Environment.EnvironmentName);

    logger.LogInformation(
        "RGB_Reporting: {ConnectionString}",
        builder.Configuration.GetConnectionString("RGB_Reporting"));

    logger.LogInformation(
        "Website: {ConnectionString}",
        builder.Configuration.GetConnectionString("Website"));
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseCors("RGBReportingClient");

app.UseAuthorization();

app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();

static void ConfigureIntegratedSecurity(
    WebApplicationBuilder builder,
    string connectionName)
{
    var connectionString =
        builder.Configuration.GetConnectionString(connectionName);

    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return;
    }

    var sb = new SqlConnectionStringBuilder(connectionString)
    {
        IntegratedSecurity = true,
        TrustServerCertificate = true
    };

    sb.Remove("User ID");
    sb.Remove("Password");

    builder.Configuration[$"ConnectionStrings:{connectionName}"]
        = sb.ConnectionString;
}