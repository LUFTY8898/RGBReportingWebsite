using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using RGBReportingWebsite.Models;

namespace RGBReportingWebsite.Services
{
    public class ReportDetailsService
    {
        private readonly HttpClient _httpClient;
        private readonly NavigationManager _navigation;


        public ReportDetailsService(
            HttpClient httpClient,
            NavigationManager navigation)
        {
            _httpClient = httpClient;
            _navigation = navigation;
        }

        // Returns raw response body from the userContext endpoint for diagnostic purposes
        public async Task<string?> GetUserContextRawAsync(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return null;

            string url = $"api/ReportDetails/userContext?login={Uri.EscapeDataString(login)}";

            using var resp = await _httpClient.GetAsync(url);
            var text = await resp.Content.ReadAsStringAsync();

            // If request routed to SPA host, attempt fallback to local API port
            if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var baseUri = new Uri(_navigation.BaseUri);
                    var builder = new UriBuilder(baseUri)
                    {
                        Port = 7278,
                        Path = "api/ReportDetails/userContext",
                        Query = "login=" + Uri.EscapeDataString(login)
                    };

                    using var fb = await _httpClient.GetAsync(builder.Uri);
                    return await fb.Content.ReadAsStringAsync();
                }
                catch
                {
                    return text;
                }
            }

            return text;
        }

        public async Task<UserContext?> GetUserContextAsync(string login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return null;

            string url = $"api/ReportDetails/userContext?login={Uri.EscapeDataString(login)}";

            using var resp = await _httpClient.GetAsync(url);
            if (!resp.IsSuccessStatusCode)
                return null;

            var text = await resp.Content.ReadAsStringAsync();
            try
            {
                var ctx = JsonSerializer.Deserialize<UserContext>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return ctx;
            }
            catch
            {
                return null;
            }
        }


        // ============================================================
        // GET REPORT DETAILS
        //
        // Returns the reports that the logged-in user is
        // permitted to access.
        // ============================================================

        public async Task<List<ReportDetail>>
            GetReportDetailsAsync(
                string userID)
        {
            if (string.IsNullOrWhiteSpace(userID))
            {
                return new List<ReportDetail>();
            }


            string url =
                "api/ReportDetails" +
                "?userID=" +
                Uri.EscapeDataString(
                    userID);


            using var resp = await _httpClient.GetAsync(url);

            var text = await resp.Content.ReadAsStringAsync();

            // If the server returned the client index.html (SPA) then the request
            // was routed to the client host rather than the API server. Attempt a
            // fallback to the local server port used by Visual Studio (7278).
            if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(text) &&
                text.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var baseUri = new Uri(_navigation.BaseUri);
                    var builder = new UriBuilder(baseUri)
                    {
                        Port = 7278,
                        Path = "api/ReportDetails",
                        Query = "userID=" + Uri.EscapeDataString(userID)
                    };

                    using var fallbackResp = await _httpClient.GetAsync(builder.Uri);
                    var fallbackText = await fallbackResp.Content.ReadAsStringAsync();

                    if (!fallbackResp.IsSuccessStatusCode)
                        throw new InvalidOperationException($"API fallback returned {(int)fallbackResp.StatusCode} {fallbackResp.ReasonPhrase}: {fallbackText}");

                    try
                    {
                        var reports = JsonSerializer.Deserialize<List<ReportDetail>>(fallbackText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        return reports ?? new List<ReportDetail>();
                    }
                    catch (JsonException jex)
                    {
                        throw new InvalidOperationException($"Failed to parse JSON response from fallback {builder.Uri}: {jex.Message}. Response body: {fallbackText}");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Initial API returned HTML and fallback failed: {ex.Message}. Initial response body: {text}");
                }
            }

            if (!resp.IsSuccessStatusCode)
            {
                // Surface server error body for easier debugging
                throw new InvalidOperationException($"API returned {(int)resp.StatusCode} {resp.ReasonPhrase}: {text}");
            }

            try
            {
                var reports = JsonSerializer.Deserialize<List<ReportDetail>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return reports ?? new List<ReportDetail>();
            }
            catch (JsonException jex)
            {
                throw new InvalidOperationException($"Failed to parse JSON response from {url}: {jex.Message}. Response body: {text}");
            }
        }


        public async Task<List<AccountManagerOption>> GetAccountManagersAsync(int reportDetailID, string login)
        {
            if (reportDetailID <= 0 || string.IsNullOrWhiteSpace(login))
                return new List<AccountManagerOption>();

            var url = $"api/ReportDetails/accountManagers?reportDetailID={reportDetailID}&login={Uri.EscapeDataString(login)}";
            using var response = await _httpClient.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode && body.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase))
            {
                var fallbackUri = new UriBuilder(new Uri(_navigation.BaseUri))
                {
                    Port = 7278,
                    Path = "api/ReportDetails/accountManagers",
                    Query = $"reportDetailID={reportDetailID}&login={Uri.EscapeDataString(login)}"
                }.Uri;

                using var fallbackResponse = await _httpClient.GetAsync(fallbackUri);
                body = await fallbackResponse.Content.ReadAsStringAsync();
                if (!fallbackResponse.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Account managers API returned {(int)fallbackResponse.StatusCode}: {body}");
            }
            else if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Account managers API returned {(int)response.StatusCode}: {body}");
            }

            try
            {
                return JsonSerializer.Deserialize<List<AccountManagerOption>>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<AccountManagerOption>();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Could not parse account managers API response: {body}", ex);
            }
        }

        // ============================================================
        // GET MANUFACTURERS
        //
        // ManufacturerController retrieves these from OMRC.
        //
        // FirmCode:
        //     Value used for report filtering.
        //
        // FirmName:
        //     Friendly name displayed in the dropdown.
        //
        // "All Manufacturers" is NOT added here.
        //
        // RunReport.razor adds that as the first selection
        // with a value of "ALL".
        // ============================================================

        public async Task<List<Manufacturer>>
            GetManufacturersAsync()
        {
            const string url =
                "api/Manufacturer";


            List<Manufacturer>? manufacturers =
                await _httpClient
                    .GetFromJsonAsync<List<Manufacturer>>(
                        url);


            return manufacturers
                ?? new List<Manufacturer>();
        }

        public async Task<List<Models.AccountManagerOption>> GetAccountManagersAsync(int userID, int reportDetailID)
        {
            string url = $"api/Dataset/{reportDetailID}/accountManagers?userID={Uri.EscapeDataString(userID.ToString())}";

            using var resp = await _httpClient.GetAsync(url);
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"API returned {(int)resp.StatusCode} {resp.ReasonPhrase}: {text}");
            }

            try
            {
                var list = JsonSerializer.Deserialize<List<Models.AccountManagerOption>>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return list ?? new List<Models.AccountManagerOption>();
            }
            catch (JsonException jex)
            {
                throw new InvalidOperationException($"Failed to parse JSON response from {url}: {jex.Message}. Response body: {text}");
            }
        }
    }
}