using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.Services;

public interface ICsrfService
{
    string GenerateToken();
    string ComputeSignature(string userId, string csrfToken);
    bool Validate(string userId, string csrfToken, string? signatureFromCookie);
    void IssueCsrf(HttpResponse response, string userId, out string csrfToken);
}
