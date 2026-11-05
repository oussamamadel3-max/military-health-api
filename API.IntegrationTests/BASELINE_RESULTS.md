# MH-SEC-051 … MH-SEC-055 HTTP Baseline Results

> Documented from actual `dotnet test` runs. Known-gap failures are intentional evidence for later fix cards — do not weaken assertions.

## Environment

- Host: `WebApplicationFactory<Program>` / `ASPNETCORE_ENVIRONMENT=Testing`
- DB: isolated temp SQLite (`%TEMP%\mh-sec-051-*.sqlite`) via open `SqliteConnection` + script bootstrap (PostgreSQL casts stripped)
- Not used: Dev/Prod `DefaultConnection`, Docker, Testcontainers (Docker unavailable on the verification machine)
- Cookies: Testing uses non-Secure cookies over HTTP TestServer (`AuthCookieService` + `CookieSecurityExtensions`)
- Rate limit isolation: each `AuthHttpSession` sends a unique `X-Forwarded-For` (loopback trusted)

## Commands

```bash
dotnet test API.IntegrationTests/API.IntegrationTests.csproj -c Release --logger "trx;LogFileName=mh-sec-055.trx"
dotnet build Backend.sln -c Release
```

## Run record after MH-SEC-055 (2026-11-05)

**Totals:** Passed **20** / Failed **1** / Total **21**

### Passed (includes MH-SEC-052/053/054/055 regressions)

- Previous green baselines + Path dual-delete + password reuse
- `Refresh_rate_limit_returns_429_after_testing_permit_limit` *(MH-SEC-055)* — Testing Refresh PermitLimit=2 → 3rd call 429 + `rate_limit_exceeded`
- `Invalid_refresh_within_limit_returns_401_not_429` *(MH-SEC-055)*

### Failed (documented KnownGap — still expected)

| Test | Trait | Owner card |
|------|-------|------------|
| `Locked_account_login_should_look_like_unknown_user_401` | KnownGap:MH-SEC-061 | MH-SEC-061 |

## Historical notes

- **MH-SEC-051:** **13 Passed / 6 Failed**
- **MH-SEC-052:** **16 Passed / 4 Failed**
- **MH-SEC-053:** **17 Passed / 3 Failed**
- **MH-SEC-054:** **18 Passed / 2 Failed**
- **MH-SEC-055:** D3 approved (5/60 Prod, 2/60 Testing, IP partition, NAT accepted, no Redis)
