using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Common.Security;
using Application.Dtos.AuthDtos;
using Application.Services.AuthServices;

namespace API.IntegrationTests.Infrastructure;

public sealed class AuthHttpSession
{
    private static int _forwardedIpCounter;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CookieContainer _cookies = new();
    private readonly HttpClient _client;
    private readonly string _forwardedClientIp;

    public AuthHttpSession(AuthWebApplicationFactory factory)
    {
        // Isolate in-memory rate-limit partitions across tests (MH-SEC-055): each session gets a unique
        // X-Forwarded-For. Loopback is trusted by default ForwardedHeaders, so RemoteIpAddress updates.
        var n = Interlocked.Increment(ref _forwardedIpCounter);
        _forwardedClientIp = $"198.51.100.{(n % 250) + 1}";

        // TestServer requires a DelegatingHandler chain; CookieContainer is applied manually.
        _ = factory.Services;
        _client = new HttpClient(new CookieContainerHandler(_cookies, _forwardedClientIp)
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
    public string ForwardedClientIp => _forwardedClientIp;
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

    public async Task<HttpResponseMessage> SelectCenterAsync(Guid centerId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/centers/select")
        {
            Content = JsonContent.Create(new { centerId })
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        await CaptureCsrfFromBodyAsync(response);
        return response;
    }

    public async Task<HttpResponseMessage> AssignUserClinicAsync(string userId, Guid centerId, Guid clinicId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/AssignUserClinic")
        {
            Content = JsonContent.Create(new { userId, centerId, clinicId })
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> RevokeUserClinicAsync(Guid assignmentId, string reason)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/RevokeUserClinic")
        {
            Content = JsonContent.Create(new { assignmentId, reason })
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
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

    public async Task<HttpResponseMessage> PatchUserStatusAsync(string userId, bool isActive)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/User/UpdateUserStatus/{Uri.EscapeDataString(userId)}")
        {
            Content = JsonContent.Create(new { isActive })
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> UpdateMyProfileAsync(string fullName, string email, string employeeNumber, string rankOrTitle)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/User/UpdateMyProfile")
        {
            Content = JsonContent.Create(new
            {
                fullName,
                email,
                employeeNumber,
                rankOrTitle
            })
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> ToggleRolesAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/ToggleRolesForUser")
        {
            Content = JsonContent.Create(body)
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> PatchUserAsync(string userId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/User/PatchUser/{Uri.EscapeDataString(userId)}")
        {
            Content = JsonContent.Create(body)
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> CreateRoleAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/Roles/Create")
        {
            Content = JsonContent.Create(body)
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> CreateUserAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/User/Create")
        {
            Content = JsonContent.Create(body)
        };
        AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public async Task<HttpResponseMessage> PostRawJsonAsync(string path, string? json, string? contentType = "application/json")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (json is null)
            request.Content = null;
        else
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, contentType ?? "application/json");
        if (path.StartsWith("/api/User", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/Roles", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/centers/select", StringComparison.OrdinalIgnoreCase))
            AttachCsrf(request);
        var response = await _client.SendAsync(request);
        LastResponse = response;
        return response;
    }

    public void DropRefreshCookie()
        => CookieHeaderAssertions.RemoveCookie(_cookies, _client.BaseAddress!, AuthCookieNames.RefreshToken);

    public void DropAccessCookie()
        => CookieHeaderAssertions.RemoveCookie(_cookies, _client.BaseAddress!, AuthCookieNames.AccessToken);

    public bool HasCookie(string name)
        => CookieHeaderAssertions.EnumerateCookies(_cookies, _client.BaseAddress!, name)
            .Any(c => !c.Expired && !string.IsNullOrEmpty(c.Value));

    public string? GetCookieValue(string name)
        => CookieHeaderAssertions.TryGetCookieValue(_cookies, _client.BaseAddress!, name);

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
        private readonly string _forwardedClientIp;

        public CookieContainerHandler(CookieContainer cookies, string forwardedClientIp)
        {
            _cookies = cookies;
            _forwardedClientIp = forwardedClientIp;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            var cookieHeader = _cookies.GetCookieHeader(uri);
            if (!string.IsNullOrEmpty(cookieHeader))
                request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

            request.Headers.Remove("X-Forwarded-For");
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", _forwardedClientIp);

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

            // Empty value = browser delete for that Path (MH-SEC-054 dual-delete).
            if (string.IsNullOrEmpty(pair[1]))
            {
                foreach (var existing in _cookies.GetCookies(uri).Cast<Cookie>()
                             .Where(c => c.Name.Equals(pair[0], StringComparison.OrdinalIgnoreCase)
                                         && c.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                             .ToList())
                {
                    existing.Expired = true;
                }

                return;
            }

            _cookies.Add(uri, new Cookie(pair[0], pair[1], path)
            {
                HttpOnly = httpOnly,
                Secure = false
            });
        }
    }
}
