# OpenIddictUI — Complete System Design Specification

## 1. Architecture Overview

### 1.1 Solution Structure

```
OpenIddictUI.sln
├── src/OpenIddictUI/        # Security Token Service (STS)
├── src/OpenIddictUI.Api/    # Protected WebAPI (JWT + scope validation)
├── frontend/                # STS Admin SPA (Vue 3, login/consent/logout)
└── client-spa/              # Demo SPA Client (oidc-client-ts, OIDC Relying Party)
```

### 1.2 Technology Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 10 / ASP.NET Core |
| Authorization Server | OpenIddict 7.x (Server + Validation + EF Core) |
| User Management | ASP.NET Core Identity |
| Database | PostgreSQL (EF Core + snake_case naming) |
| Caching | HybridCache (L1 memory + L2 PostgreSQL distributed) |
| Logging | Serilog (Console + File sinks, config in serilog.json) |
| Frontend | Vue 3 + TypeScript + Vite (history mode SPA) |
| SPA Auth Library | oidc-client-ts |
| Captcha | SkiaSharp |

### 1.3 Middleware Pipeline (Order-Sensitive)

```
UseDeveloperExceptionPage (dev)
  → DecryptRequestMiddleware (AES-ECB request body)
  → UseHealthChecks("/healthz")
  → UseCookiePolicy
  → UseDefaultFiles / UseStaticFiles
  → UseRouting
  → UseAntiforgery
  → UseCors
  → UseSession
  → UseAuthentication
  → UseAuthorization
  → MapControllers
  → MapFallbackToFile("index.html")
  → UseCloudEvents (Dapr, conditional)
  → PluginLoader.Use
```

## 2. OpenID Connect / OAuth Implementation

### 2.1 Endpoints

| Endpoint | Method | Passthrough | Handler |
|----------|--------|-------------|---------|
| `/connect/authorize` | GET/POST | ✅ `EnableAuthorizationEndpointPassthrough` | `AuthorizationController.Authorize()` |
| `/connect/token` | POST | ✅ `EnableTokenEndpointPassthrough` | `AuthorizationController.Exchange()` |
| `/connect/logout` | GET/POST | ✅ `EnableEndSessionEndpointPassthrough` | `AuthorizationController.Logout()` |

### 2.2 Grant Types

| Grant | Handler | Permission String |
|-------|---------|-------------------|
| `authorization_code` | `AuthorizationCodeGrantHandler` | `GrantTypes.AuthorizationCode` |
| `password` | `PasswordGrantHandler` | `GrantTypes.Password` |
| `phone_code` (custom) | `PhoneCodeGrantHandler` | `Prefixes.GrantType + "phone_code"` |
| `refresh_token` | `AuthorizationCodeGrantHandler` (shared) | `GrantTypes.RefreshToken` |

### 2.3 Grant Handler Architecture

All handlers implement `IGrantHandler` interface and extend `BaseGrantHandler`.

- **BaseGrantHandler**: provides `Success(principal)`, `Failure(description)`, `GetDestinations(claim)`, `ExecuteAsync()`
- **GrantResult**: return type `{ Principal?, Error?, ErrorDescription? }`
- **GrantHandlerResolver**: builds `Dictionary<string, IGrantHandler>` from DI `IEnumerable<IGrantHandler>`, O(1) lookup
- **Stateless**: handlers resolve dependencies from `context.RequestServices` at invoke time (any DI lifetime)
- **Plugin registration**: `builder.Services.AddSingleton<IGrantHandler, MyHandler>()`

### 2.4 Authorize Flow

```
GET /connect/authorize?client_id=spa-client&response_type=code&scope=openid+profile+api1&redirect_uri=...
    │
    ├─ ① OpenIddict validates (client_id, redirect_uri, PKCE, scopes) — passthrough gate
    ├─ ② Controller checks client exists + enabled (Settings["enabled"] != "false")
    ├─ ③ AuthenticateAsync() — default scheme (Identity.Application cookie)
    │   ├── Not authenticated + prompt=none → Forbid (login_required)
    │   └── Not authenticated → Challenge → 302 /account/login?returnUrl=...
    ├─ ④ GetUserAsync(principal) — verify user exists in DB
    ├─ ⑤ Check max_age — force re-auth if session too old
    ├─ ⑥ Check prompt=login — SignOut + Challenge
    ├─ ⑦ Check Consent:
    │   ├── ConsentType != Explicit → auto-grant (skip consent)
    │   ├── ConsentType == Explicit + existing authorization → skip consent
    │   └── ConsentType == Explicit + no authorization → cache entry → Redirect /consent/{id}
    └─ ⑧ CreateUserPrincipalAsync → SetScopes → SetResources → SetDestinations → SignIn (authorization_code)
```

### 2.5 Consent Flow (Secure)

```
AuthorizationController:
  consentId = Guid.NewGuid().ToString("N")
  HybridCache.SetAsync(consentId, {ClientId, ReturnUrl, Scopes}, 10min)
  Redirect /consent/{consentId}

ConsentController GET /api/consent/{id}:
  HybridCache.GetOrCreateAsync(id) → entry
  Parse scopes → split identity vs resource
  Return clientName + scopes + clientUrl/LogoUrl (from Settings)

ConsentController POST /api/consent/{id}:
  HybridCache.GetOrCreateAsync(id) → entry
  Validate client exists + enabled
  Validate scopesConsented ⊆ entry.Scopes (prevent privilege escalation)
  GetUserAsync → CreateUserPrincipalAsync → authorizationManager.CreateAsync
  Return { location: entry.ReturnUrl }
```

**Key Security**: Client receives only `consentId` (random), never `returnUrl`. Original request data stays in server-side HybridCache. Prevents tampering of client_id, scope, and redirect_uri.

### 2.6 Token Exchange

```
POST /connect/token
    GrantHandlerResolver.GetHandler(grant_type)
    → handler.ExecuteAsync(request, context)
    → GrantResult → BaseGrantHandler → SignInResult / ForbidResult
```

### 2.7 RP-Initiated Logout

```
GET /connect/logout?id_token_hint=...&post_logout_redirect_uri=...&state=...
    Logout():
      SignOutAsync(Identity.ApplicationScheme)  — clear Identity cookie
      SignOut(OpenIddict scheme)               — revoke tokens + sign-out iframe + 302 redirect
```

## 3. Account Management API

### 3.1 Endpoints

| Method | Endpoint | Auth | CSRF | Purpose |
|--------|----------|------|------|---------|
| POST | `/account/login` | None | ✅ | Password login → `{ location }` |
| POST | `/account/loginByCode` | None | ✅ | SMS code login → `{ location }` |
| POST | `/account/sendCode` | None | ❌ | Send SMS code (60s rate limit, captcha) |
| POST | `/account/logout` | None | ❌ | Clear Identity cookie → `{ location }` |
| POST | `/account/resetPwdByOriginPwd` | None | ❌ | Change password with old password |
| POST | `/account/resetPwd` | None | ❌ | Reset password via phone code |

### 3.2 CSRF Protection

- **Header-based**: `X-XSRF-TOKEN` (configurable in `AddAntiforgery`)
- **Cookie-based**: `XSRF-TOKEN` (SameSite=Lax)
- **Frontend**: `useFetch.ts` auto-reads cookie, auto-retries on invalid token
- **Validation**: Manual `await antiforgery.ValidateRequestAsync(HttpContext)` in controllers
- **Exemptions**: `sendCode` (no state change), `logout` (clear cookie only), `resetPwd*`

### 3.3 Rate Limiting

SMS send: HybridCache key `SMS:PHONE:{number}` with 60s TTL. Frontend also enforces 60s cooldown UI lock.

## 4. Data Model

### 4.1 Database Tables

| Table | Source | Managed By |
|-------|--------|-----------|
| `wild_goose_user` (configurable) | ASP.NET Core Identity | External system (`ExcludeFromMigrations`) |
| `wild_goose_role` | ASP.NET Core Identity | External system |
| `openiddict_applications` | OpenIddict EF Core | EF Migration |
| `openiddict_scopes` | OpenIddict EF Core | EF Migration |
| `openiddict_authorizations` | OpenIddict EF Core | EF Migration |
| `openiddict_tokens` | OpenIddict EF Core | EF Migration |
| `openiddict_migrations_history` (configurable) | EF Core | EF Migration |
| `cache_entries` | Custom (`PostgreSqlDistributedCache`) | Auto-created |

### 4.2 Naming Convention

All tables/columns: **lowercase snake_case**. Implemented via `ApplySnakeCaseNaming()` in `AppDbContext.OnModelCreating`. Special handling for "OpenIddict" → "openiddict_" prefix.

### 4.3 Soft Delete

Configurable via `IdentityExtension.SoftDeleteColumn`. When set, EF Core global query filter automatically excludes soft-deleted users from all `UserManager` queries.

### 4.4 Seed Data

`openiddict-seed.json` contains:
```json
{
  "OpenIddictSeed": {
    "Scopes": [{"Name": "api1"}, {"Name": "api2"}],
    "Clients": [{
      "ClientId": "spa-client",
      "ConsentType": "explicit",
      "GrantTypes": ["authorization_code", "refresh_token"],
      "Scopes": ["openid", "profile", "api1"],
      "ClientUrl": "optional",
      "ClientLogoUrl": "optional"
    }]
  }
}
```

Custom properties (`Enabled`, `ClientUrl`, `ClientLogoUrl`) stored in `OpenIddictApplicationDescriptor.Settings` dictionary.

`Disabled` semantics: only `"false"` (case-insensitive) means disabled. Missing or any other value = enabled.

## 5. Frontend Architecture (STS Admin SPA)

### 5.1 Routes

| Path | Component | Guard |
|------|-----------|-------|
| `/` | → redirect `/account/login` | ✅ auth check |
| `/account/login` | `LoginPage.vue` | ✅ skip if logged in |
| `/welcome` | `WelcomePage.vue` | ❌ excluded |
| `/consent/:id` | `ConsentPage.vue` | ❌ excluded |
| `/logout` | `LogoutPage.vue` | - |
| `/logged-out` | `LoggedOutPage.vue` | ❌ excluded |
| `/error` | `ErrorPage.vue` | - |
| `/change-password` | `ChangePasswordPage.vue` | - |
| `/grants` | `GrantsPage.vue` | - |
| `*` | → redirect `/not-found` | - |

### 5.2 Auth Guard Logic

```
beforeEach:
  lowercase redirect
  hash detection → /not-found
  skip: /welcome, /logged-out, /consent/*
  load() → /session API
  if user → redirect /welcome
  else → redirect /account/login (or allow if already there)
```

### 5.3 HTTP Client (useFetch.ts)

- **XSRF auto-read**: reads `XSRF-TOKEN` cookie → header `X-XSRF-TOKEN`
- **Retry on CSRF fail**: detects "Invalid"+"token" in error → refreshes token → retries
- **Credentials**: `include` (sends cookies for cross-origin requests)

## 6. API Project (OpenIddictUI.Api)

### 6.1 Authentication

Uses `OpenIddict.Validation.AspNetCore` for automatic:
- Signing key discovery from `/.well-known/openid-configuration`
- JWE token decryption (if encryption enabled on server)
- Audience validation: `ValidAudiences = ["api1"]`

### 6.2 Authorization Policy

`"api1"` policy: `RequireAuthenticatedUser()` + `RequireAssertion(scope claim contains "api1")`. Scope claim is space-separated, split check.

### 6.3 Endpoints

| Method | Endpoint | Policy |
|--------|----------|--------|
| GET | `/api/me` | `api1` |

## 7. SPA Demo Client (client-spa)

- **Technology**: Vue 3 + oidc-client-ts
- **Flow**: `signinRedirect()` → callback `/signin-redirect-callback` → `signinCallback()` → home
- **Silent Renew**: `/signin-silent-callback` via iframe
- **Logout**: `signoutRedirect()` → OIDC RP-Initiated Logout
- **Token Storage**: `WebStorageStateStore(localStorage)`
- **API Call**: Bearer token in Authorization header

## 8. Security Design

### 8.1 Anti-Forgery (CSRF)
- Double-submit cookie pattern
- Configurable header name, cookie name, SameSite
- Token reusable (not one-time nonce)
- Automatic retry on failure

### 8.2 Request Validation
- All string inputs annotated with `[StringLength(n)]` or `[Required]`
- `returnUrl` in account endpoints: max 2048 chars (DoS prevention)
- Consent ID: max 64 chars (GUID without dashes)

### 8.3 Cookie Configuration
- Identity cookie: `idsrv`, HttpOnly, SameSite=Lax, 2h sliding
- External cookie: `idsrv.external`, HttpOnly, SameSite=Lax
- Session cookie: `session`, HttpOnly, IsEssential

### 8.4 Consent Security
- Server-side consent cache (HybridCache), client only receives random ID
- Scope validation: consented scopes must be subset of original request
- Client validation: exists + enabled before creating authorization

## 9. Plugin System

DLL-based: scans `Plugins/` directory for assemblies with types containing "Plugin" in name. Each plugin implements:
```csharp
public static class MyPlugin {
    public static void Load(IHostApplicationBuilder builder) { /* register services */ }
    public static void Use(WebApplication app) { /* register middleware */ }
}
```

Grant handler plugins register via standard DI: `builder.Services.AddSingleton<IGrantHandler, MyHandler>()`.

## 10. Configuration Files

| File | Purpose |
|------|---------|
| `appsettings.json` | Core config: conn string, Identity, cookie, service options |
| `openiddict-seed.json` | OAuth clients + scopes (loaded via `AddJsonFile`) |
| `serilog.json` | Log sinks + levels (loaded before `UseSerilog`) |
| `appsettings.Development.json` | Dev overrides |
