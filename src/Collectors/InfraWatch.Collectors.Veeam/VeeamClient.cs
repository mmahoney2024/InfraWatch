using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace InfraWatch.Collectors.Veeam;

/// <summary>
/// Thin Veeam B&amp;R REST client: OAuth2 password-grant token (cached) + the required
/// x-api-version header. The typed HttpClient (base address, self-signed cert handling) is
/// configured in the DI extension.
/// </summary>
public sealed class VeeamClient
{
    private readonly HttpClient _http;
    private readonly VeeamOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiry;

    public VeeamClient(HttpClient http, IOptions<VeeamOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        await EnsureTokenAsync(ct);
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        req.Headers.Add("x-api-version", _options.ApiVersion);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    private async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return;

        await _gate.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiry)
                return;

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = _options.Username,
                ["password"] = _options.Password,
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/oauth2/token") { Content = form };
            req.Headers.Add("x-api-version", _options.ApiVersion);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            _token = doc.RootElement.GetProperty("access_token").GetString();
            var expires = doc.RootElement.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var s) ? s : 900;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expires - 60));
        }
        finally
        {
            _gate.Release();
        }
    }
}
