# Configuration secrets (MH-FIX-080 / FR-080)

## Local development

1. Copy `API/appsettings.Local.example.json` to `API/appsettings.Local.json` (same folder).
2. Set **your** PostgreSQL connection string under `ConnectionStrings:DefaultConnection`.
3. Set a random **JwtSettings:Key** (at least 32 characters for HS256).
4. Run the API with `ASPNETCORE_ENVIRONMENT=Development` (default in `launchSettings.json`).

`appsettings.Local.json` is listed in `.gitignore` and must **never** be committed.

Load order in Development (later wins except environment variables override all):

1. `appsettings.json` (no secrets)
2. `appsettings.Development.json`
3. User secrets (optional)
4. `appsettings.Local.json` (optional, highest among JSON files in Development)
5. Environment variables
6. Command-line arguments

If secrets are missing in Development, startup fails with a message pointing to the example file—**without** printing secret values.

## Production / staging

Do **not** put secrets in tracked JSON. Provide:

| Setting | Environment variable |
|---------|----------------------|
| Database | `ConnectionStrings__DefaultConnection` |
| JWT signing key | `JwtSettings__Key` |

Optional overrides: `JwtSettings__Issuer`, `JwtSettings__Audience`, etc.

If required values are missing in Production, the application **fails at startup** with a configuration error (no secret values in logs).

## Testing

`Application.Tests` use in-memory EF contexts and do **not** read `appsettings.Local.json`. The `Testing` environment skips startup secret validation when hosting the API with `ASPNETCORE_ENVIRONMENT=Testing`.

## Git history

Connection strings and JWT keys were previously committed in `appsettings.json`. **Rotate** the database password and JWT signing key in every environment that used those values, and invalidate outstanding refresh tokens if JWT keys change.

## Verify ignore

```powershell
cd MilitaryHealth-api
git check-ignore -v API/appsettings.Local.json
```

Expected: rule `**/appsettings.Local.json` matches.
