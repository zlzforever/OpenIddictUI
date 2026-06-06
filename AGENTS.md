# OpenIddictUI — Project Knowledge Base

**Generated:** 2026-06-02 | **Commit:** afcfaad | **Branch:** main
**Stack:** .NET 10 ASP.NET Core + OpenIddict 7.x + Vue 3 + PostgreSQL + SkiaSharp + Serilog

## OVERVIEW
OAuth 2.0 / OpenID Connect STS with ASP.NET Core Identity, custom grant handler plugin system, Vue 3 admin SPA, and demo OIDC client.

## STRUCTURE
```
OpenIddictUI/
├── src/OpenIddictUI/        # STS backend (OpenIddict passthrough + Identity)
│   ├── Controllers/         # OAuth endpoints, account, consent, APIs
│   ├── Grants/              # IGrantHandler plugin system (password/phone/code)
│   ├── Sms/                 # SMS sender (AliYun / Console fallback)
│   ├── Middlewares/         # DecryptRequestMiddleware
│   ├── Plugins/             # Plugin loader (Dapr CloudEvents)
│   ├── Data/                # AppDbContext + SeedData
│   └── Migrations/          # EF Core migrations (auto-generated)
├── src/OpenIddictUI.Api/    # Protected WebAPI (JWT Bearer, 1 demo controller)
├── frontend/                # STS admin SPA (Vue 3, login/consent/logout/grants)
└── client-spa/              # Demo OIDC client (oidc-client-ts, PKCE S256)
```

## WHERE TO LOOK
| Task | Location |
|------|----------|
| OAuth authorize/token logic | `Controllers/AuthorizationController.cs` + `Grants/*.cs` |
| Login/SMS/logout/password | `Controllers/AccountController.cs` |
| OAuth consent flow | `Controllers/ConsentController.cs` |
| Custom grant types | `Grants/IGrantHandler.cs` → `Grants/BaseGrantHandler.cs` |
| Client/scope seed data | `openiddict-seed.json` → `Data/SeedData.cs` |
| OpenIddict server setup | `Program.cs` AddOpenIddict() — passthrough mode |
| Middleware pipeline | `Program.cs` — order-sensitive (see CONVENTIONS) |
| Cookie/Session/Postgres cache | `Program.cs` AddIdentity + AddSession + AddDistributedPostgresCache |
| Antiforgery (XSRF) | `Program.cs` AddAntiforgery + `frontend/src/composables/useFetch.ts` |
| SPA routing + auth guard | `frontend/src/router/index.ts` |
| OIDC client config | `client-spa/src/auth/config.ts` |
| DB schema (snake_case) | `Data/AppDbContext.cs` |

## CONVENTIONS
- **API response**: unified `ApiResult { code, success, message, data }` — see `Controllers/ApiResult.cs`
- **String params**: all must have `[StringLength]` or `[Required]`
- **if/foreach**: braces `{}` required — never omit
- **Comments**: Chinese annotations for key nodes; OAuth flow steps numbered ①②③
- **Naming**: DB tables/columns snake_case via `AppDbContext` auto-transform
- **Identity tables**: `ExcludeFromMigrations` — managed externally
- **Grant handlers**: no constructor DI — resolve from `context.RequestServices` at runtime
- **Middleware order** (order-sensitive): `DecryptRequest` → `HealthChecks(/healthz)` → `CookiePolicy` → `DefaultFiles` → `StaticFiles` → `Routing` → `Antiforgery` → `CORS` → `Session` → `Auth` → `Controllers` → `Fallback(index.html)`
- **Dev captcha skip**: `CheckCaptcha()` bypasses captcha when `IsDevelopment()`

## ANTI-PATTERNS
- Don't throw in Controllers → return `Forbid()` + `AuthenticationProperties`
- Don't call `GetOpenIddictServerRequest()` in non-passthrough endpoints
- Don't pass `returnUrl` to frontend for consent → use `HybridCache` + `ConsentEntry`, pass only id
- Don't suppress type errors (`as any`, `@ts-ignore`)

## NOTES
- **OpenIddict passthrough**: request passes built-in validation (redirect_uri, scope, PKCE) before Controller
- **Signing key**: RSA 2048 self-signed, 100yr validity
- **Access tokens**: unencrypted (jwt) via `DisableAccessTokenEncryption()` — third-party JwtBearer compatible
- **HybridCache**: captcha codes, consent entries, SMS rate limiting
- **Dapr**: CloudEvents + pub/sub auto-enabled if `DAPR_HTTP_PORT` set
- **PKCE**: S256 enforced for `spa-client`
- **CORS**: `SetIsOriginAllowed(_ => true)` in dev — production must use whitelist

## COMMANDS
```bash
# STS
dotnet run --project src/OpenIddictUI --urls http://localhost:5164

# API
dotnet run --project src/OpenIddictUI.Api --urls http://localhost:5100

# Frontend
cd frontend && npm run dev

# SPA Demo
cd client-spa && npm run dev -- --port 5175

# DB Migration
cd src/OpenIddictUI && dotnet ef database update
```
