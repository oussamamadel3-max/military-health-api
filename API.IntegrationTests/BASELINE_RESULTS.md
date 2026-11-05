# MH-SEC-051 … MH-SEC-054 HTTP Baseline Results

> Documented from actual `dotnet test` runs. Known-gap failures are intentional evidence for later fix cards — do not weaken assertions.

## Environment

- Host: `WebApplicationFactory<Program>` / `ASPNETCORE_ENVIRONMENT=Testing`
- DB: isolated temp SQLite (`%TEMP%\mh-sec-051-*.sqlite`) via open `SqliteConnection` + script bootstrap (PostgreSQL casts stripped)
- Not used: Dev/Prod `DefaultConnection`, Docker, Testcontainers (Docker unavailable on the verification machine)
- Cookies: Testing uses non-Secure cookies over HTTP TestServer (`AuthCookieService` + `CookieSecurityExtensions`)

## Commands

```bash
dotnet test API.IntegrationTests/API.IntegrationTests.csproj -c Release --logger "trx;LogFileName=mh-sec-054.trx"
dotnet build Backend.sln -c Release
```

## Run record after MH-SEC-054 (2026-11-05)

**Totals:** Passed **18** / Failed **2** / Total **20**

### Passed (includes MH-SEC-052/053/054 regressions)

- Previous MH-SEC-051/052/053 green baselines
- Login/Refresh issue `refresh_token` with `Path=/api/Auth/Refresh` + legacy `Path=/` delete on issue
- `Refresh_cookie_path_is_endpoint_scoped_and_refresh_logout_still_work` *(MH-SEC-054)* — Refresh+Logout OK; dual-delete on Logout

### Failed (documented KnownGap — still expected)

| Test | Trait | Owner card |
|------|-------|------------|
| `Refresh_endpoint_should_return_429_when_rate_limit_exceeded` | KnownGap:MH-SEC-055 | MH-SEC-055 |
| `Locked_account_login_should_look_like_unknown_user_401` | KnownGap:MH-SEC-061 | MH-SEC-061 |

## Historical notes

- **MH-SEC-051:** **13 Passed / 6 Failed**
- **MH-SEC-052:** **16 Passed / 4 Failed**
- **MH-SEC-053:** **17 Passed / 3 Failed**
- **MH-SEC-054:** F-12/D2 approved by owner as `/api/Auth/Refresh`; KnownGap Path test moved to green Regression
