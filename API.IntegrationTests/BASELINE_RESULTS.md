# MH-SEC-051 Baseline Results

> Documented from actual `dotnet test` runs. Known-gap failures are intentional evidence for later fix cards — do not weaken assertions.

## Environment

- Host: `WebApplicationFactory<Program>` / `ASPNETCORE_ENVIRONMENT=Testing`
- DB: isolated temp SQLite (`%TEMP%\mh-sec-051-*.sqlite`) via open `SqliteConnection` + script bootstrap (PostgreSQL casts stripped)
- Not used: Dev/Prod `DefaultConnection`, Docker, Testcontainers (Docker unavailable on the verification machine)
- Cookies: Testing uses non-Secure cookies over HTTP TestServer (`AuthCookieService` + `CookieSecurityExtensions`)

## Commands

```bash
dotnet test API.IntegrationTests/API.IntegrationTests.csproj -c Release --logger "trx;LogFileName=mh-sec-051.trx"
dotnet build Backend.sln -c Release
```

## Run record (2026-11-05)

**Totals:** Passed **13** / Failed **6** / Total **19**

### Passed (baseline green)

- `Factory_database_path_is_temp_isolated_sqlite_file`
- `Login_success_sets_httponly_cookies_without_tokens_in_body`
- `Login_failure_does_not_issue_auth_cookies`
- `Login_unknown_user_returns_401_without_session`
- `Refresh_rotates_cookies_and_rejects_reuse`
- `Logout_with_access_and_refresh_ends_current_family_access`
- `Logout_repeated_does_not_return_500`
- `Two_sessions_for_same_user_are_isolated_on_full_cookie_logout`
- `Disabled_user_cannot_login_and_reactivation_restores_login`
- `Change_password_success_invalidates_prior_session`
- `Admin_reset_password_requires_permission_and_succeeds_for_admin`
- `Unauthenticated_me_is_401_not_403`
- `Audit_login_and_logout_do_not_store_password_or_raw_tokens`

### Failed (documented KnownGap — expected until fix cards)

| Test | Trait | Owner card |
|------|-------|------------|
| `Logout_without_refresh_cookie_should_reject_subsequent_access` | KnownGap:MH-SEC-052 | MH-SEC-052 |
| `Logout_without_refresh_cookie_should_reject_subsequent_refresh_of_dropped_family` | KnownGap:MH-SEC-052 | MH-SEC-052 |
| `Change_password_should_reject_reusing_current_password` | KnownGap:MH-SEC-053 | MH-SEC-053 |
| `Refresh_cookie_path_should_be_refresh_endpoint_not_root` | KnownGap:MH-SEC-054 | MH-SEC-054 |
| `Refresh_endpoint_should_return_429_when_rate_limit_exceeded` | KnownGap:MH-SEC-055 | MH-SEC-055 |
| `Locked_account_login_should_look_like_unknown_user_401` | KnownGap:MH-SEC-061 | MH-SEC-061 |

## Notes

- Gap-052 access test re-attaches the **pre-logout** access cookie after Logout so failure proves missing **server** family revoke (not merely client `Set-Cookie` deletes).
- Closing MH-SEC-051 does **not** require KnownGap tests to be green.
- MH-SEC-050 report was not present in-repo; implementation used live code contracts.
