using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Services.AuthServices;

namespace API.IntegrationTests.Infrastructure;

public sealed class AuthHttpSession
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _client;

    public AuthHttpSession(AuthWebApplicationFactory factory)
    {
        // TestServer requires a DelegatingHandler chain; CookieContainer is applied manually.
        _ = factory.Services;
        _client = new HttpClient(new CookieContainerHandler(_cookies)
        {
            InnerHandler = factory.Server.CreateHandler()
        })
        {
            BaseAddress = factory.Server.BaseAddress,
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    public CookieContainer Cookies => _cookies;
    public HttpClient Client => _client;
    public string? LastCsrfToken { get; private set; }
    public HttpResponseMessage? LastResponse { get; private set; }

    public async Task<HttpResponseMessage> LoginAsync(string userName, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/Auth/Login", new RequestToken
        {
            UserName = userName,
            PassWord = password
        });
        LastResponse = response;
        await CaptureCsrfFromBodyAsync(response);
        return response;
    }

    public async Task<HttpResponseMessage> RefreshAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/Refresh");
        var response = await _client.SendAsync(request);
        LastResponse = response;
        await CaptureCsrfFromBodyAsync(response);
        return response;
    }

    public async Task<HttpResponseMessage> LogoutAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/Logout");
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> MeAsync()
    {
        var response = await _client.GetAsync("/api/Auth/me");
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> ChangeMyPasswordAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/ChangeMyPassword")
        {
            Content = JsonContent.Create(new ChangePasswordRequest
            {
                CurrentPassword = currentPassword,
                NewPassword = newPassword,
                ConfirmPassword = confirmPassword
            })
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> AdminResetPasswordAsync(string userId, string newPassword)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/ChangePasswordUser")
        {
            Content = JsonContent.Create(new AdminChangePasswordRequest
            {
                UserId = userId,
                NewPassword = newPassword,
                ConfirmPassword = newPassword
            })
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> ForbiddenAdminResetProbeAsync(string targetUserId)
    {
        // Requires UserManagement.ResetPassword — used to distinguish 401 vs 403.
        return await AdminResetPasswordAsync(targetUserId, "N3w!ProbePass1");
    }

    public void DropRefreshCookie()
        => CookieHeaderAssertions.RemoveCookie(_cookies, _client.BaseAddress!, AuthCookieNames.RefreshToken);

    public void DropAccessCookie()
        => CookieHeaderAssertions.RemoveCookie(_cookies, _client.BaseAddress!, AuthCookieNames.AccessToken);

    public bool HasCookie(string name)
        => _cookies.GetCookies(_client.BaseAddress!).Cast<Cookie>().Any(c => c.Name == name && !c.Expired);

    public async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
    }

    public async Task AssertNoSecretsInBodyAsync(HttpResponseMessage response, params string[] extras)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Bearer ", body, StringComparison.OrdinalIgnoreCase);
        foreach (var secret in extras)
        {
            if (!string.IsNullOrEmpty(secret))
                Assert.DoesNotContain(secret, body, StringComparison.Ordinal);
        }
    }

    private void AttachCsrf(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(LastCsrfToken))
            request.Headers.TryAddWithoutValidation(CsrfMutationGuard.HeaderName, LastCsrfToken);
    }

    private async Task CaptureCsrfFromBodyAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            return;

        using var doc = await ReadJsonAsync(response);
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("csrfToken", out var csrf)
            && csrf.ValueKind == JsonValueKind.String)
        {
            LastCsrfToken = csrf.GetString();
        }
    }

    private sealed class CookieContainerHandler : DelegatingHandler
    {
        private readonly CookieContainer _cookies;

        public CookieContainerHandler(CookieContainer cookies) => _cookies = cookies;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            var cookieHeader = _cookies.GetCookieHeader(uri);
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                    ApplySetCookie(uri, setCookie);
            }

            return response;
        }

        private void ApplySetCookie(Uri uri, string setCookie)
        {
            // CookieContainer.SetCookies can reject TestServer cookies; parse name/value/path explicitly.
            var segments = setCookie.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return;

            var pair = segments[0].Split('=', 2);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]))
                return;

            var path = "/";
            var httpOnly = false;
            foreach (var segment in segments.Skip(1))
            {
                if (segment.StartsWith("path=", StringComparison.OrdinalIgnoreCase))
                    path = segment["path=".Length..];
                else if (segment.Equals("httponly", StringComparison.OrdinalIgnoreCase))
                    httpOnly = true;
            }

            _cookies.Add(uri, new Cookie(pair[0], pair[1], path)
            {
                HttpOnly = httpOnly,
                Secure = false
            });
        }
    }
}
