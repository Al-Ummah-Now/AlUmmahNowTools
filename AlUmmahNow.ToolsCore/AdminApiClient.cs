using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlUmmahNow.ToolsCore;

/// <summary>Who the signed-in account is to admin-api (<c>GET /api/admin/import/me</c>).</summary>
public sealed record StaffProfile(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("roles")] IReadOnlyList<string> Roles,
    [property: JsonPropertyName("permissions")] IReadOnlyList<string> Permissions)
{
    public bool Can(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);
}

public enum StaffCheckOutcome
{
    /// <summary>The account holds <c>imports:manage</c>: the app opens</summary>
    Staff,
    /// <summary>Signed in, but not staff (403): "This app is for Al Ummah Now staff"</summary>
    NotStaff,
    /// <summary>The token was refused (401): sign in again</summary>
    SignedOut,
    /// <summary>admin-api could not be reached or failed: try again</summary>
    Unreachable,
}

public sealed record StaffCheck(StaffCheckOutcome Outcome, StaffProfile? Profile = null);

/// <summary>The admin API calls of the app, with the account's access token.</summary>
public sealed class AdminApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly Uri _base;

    public AdminApiClient(Uri adminApi, HttpMessageHandler? handler = null)
    {
        _base = adminApi;
        _http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AlUmmahNowTools/1.0");
    }

    public async Task<StaffCheck> CheckStaffAsync(string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_base, "/api/admin/import/me"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new StaffCheck(StaffCheckOutcome.Unreachable);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized) return new StaffCheck(StaffCheckOutcome.SignedOut);
            if (response.StatusCode == HttpStatusCode.Forbidden) return new StaffCheck(StaffCheckOutcome.NotStaff);
            if (!response.IsSuccessStatusCode) return new StaffCheck(StaffCheckOutcome.Unreachable);
            try
            {
                var body = await response.Content.ReadFromJsonAsync<Envelope>(Json, ct).ConfigureAwait(false);
                var profile = body?.Result;
                if (profile is null) return new StaffCheck(StaffCheckOutcome.Unreachable);
                return profile.Can("imports:manage") ? new StaffCheck(StaffCheckOutcome.Staff, profile) : new StaffCheck(StaffCheckOutcome.NotStaff, profile);
            }
            catch (JsonException)
            {
                return new StaffCheck(StaffCheckOutcome.Unreachable);
            }
        }
    }

    private sealed record Envelope([property: JsonPropertyName("result")] StaffProfile? Result);
}
