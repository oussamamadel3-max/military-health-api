# MH-SEC-051 / MH-SEC-052 HTTP Baseline Results

> Documented from actual `dotnet test` runs. Known-gap failures are intentional evidence for later fix cards — do not weaken assertions.

## Environment

- Host: `WebApplicationFactory<Program>` / `ASPNETCORE_ENVIRONMENT=Testing`
- DB: isolated temp SQLite (`%TEMP%\mh-sec-051-*.sqlite`) via open `SqliteConnection` + script bootstrap (PostgreSQL casts stripped)
- Not used: Dev/Prod `DefaultConnection`, Docker, Testcontainers (Docker unavailable on the verification machine)
- Cookies: Testing uses non-Secure cookies over HTTP TestServer (`AuthCookieService` + `CookieSecurityExtensions`)

## Commands

```bash
dotnet test API.IntegrationTests/API.IntegrationTests.csproj -c Release --logger "trx;LogFileName=mh-sec-052.trx"
dotnet build Backend.sln -c Release
```

## Run record after MH-SEC-052 (2026-11-05)

**Totals:** Passed **16** / Failed **4** / Total **20**

### Passed (includes MH-SEC-052 regressions)

- Previous MH-SEC-051 green baselines (login/refresh/logout/audit/401…)
- `Logout_without_refresh_cookie_rejects_reattached_access_token` *(MH-SEC-052)*
- `Logout_without_refresh_cookie_rejects_replayed_refresh_token` *(MH-SEC-052)*
- `Logout_without_refresh_on_first_device_leaves_second_device_session_alive` *(MH-SEC-052)*

### Failed (documented KnownGap — still expected)

| Test | Trait | Owner card |
|------|-------|------------|
| `Change_password_should_reject_reusing_current_password` | KnownGap:MH-SEC-053 | MH-SEC-053 |
| `Refresh_cookie_path_should_be_refresh_endpoint_not_root` | KnownGap:MH-SEC-054 | MH-SEC-054 |
| `Refresh_endpoint_should_return_429_when_rate_limit_exceeded` | KnownGap:MH-SEC-055 | MH-SEC-055 |
| `Locked_account_login_should_look_like_unknown_user_401` | KnownGap:MH-SEC-061 | MH-SEC-061 |

## Historical MH-SEC-051 note

Before MH-SEC-052 the suite was **13 Passed / 6 Failed**; the two MH-SEC-052 KnownGap failures are now green after server-side family revoke on Logout.
