namespace Application.Dtos.AuthDtos;

public class AuthDevelopmentSettings
{
    public const string SectionName = "Auth";

    /// <summary>
    /// Development only: skip CSRF header validation (Swagger / Postman).
    /// </summary>
    public bool DisableCsrfValidation { get; set; }

    /// <summary>
    /// Development only: accept JWT from Authorization: Bearer header.
    /// </summary>
    public bool AllowBearerTokenInDevelopment { get; set; }
}
