# OpenIddictUI Backend Design — SecurityTokenService Rewrite

## 1. Overview

Rewrite the SecurityTokenService using **OpenIddict 6.x** instead of IdentityServer4, while maintaining API compatibility for the existing pure HTML+JS login frontend. The backend provides:
- OpenID Connect / OAuth 2.0 authorization server (authorization_code, password, phone_code grants)
- User authentication (password + SMS verification code)
- OAuth consent management
- Session management and logout
- Captcha generation
- DLL-based plugin system
- EF Core migrations for Identity + OpenIddict stores

**Frontend**: Already built in `wwwroot/` — pure HTML+JS SPA with hash routing, AES-ECB request encryption, and tabbed login (password / SMS / external providers).

## 2. Architecture

### 2.1 Project Structure

```
OpenIddictUI.sln
├── src/
│   └── OpenIddictUI/                # ASP.NET Core Web Application
│       ├── Program.cs               # Host, DI, middleware pipeline
│       ├── appsettings.json         # Connection strings, certs, options
│       ├── Controllers/
│       │   ├── AuthorizationController.cs  # /connect/authorize, /connect/token
│       │   ├── AccountController.cs        # /account/login, /account/loginByCode, /account/sendCode, /account/logout
│       │   ├── ConsentController.cs        # /consent API
│       │   ├── SessionController.cs        # /session
│       │   └── CaptchaController.cs        # /api/v1.0/captcha/generate
│       ├── Data/
│       │   ├── AppDbContext.cs              # Identity + OpenIddict EF Core DbContext
│       │   └── SeedData.cs                 # Client/scope/user seeding
│       ├── Identity/
│       │   └── User.cs                     # IdentityUser extension
│       ├── Grants/
│       │   └── PhoneCodeGrantHandler.cs    # Custom phone_code grant validation
│       ├── Sms/
│       │   ├── ISmsSender.cs
│       │   └── AliYunSmsSender.cs
│       ├── Options/
│       │   └── ServiceOptions.cs
│       ├── Middlewares/
│       │   └── DecryptRequestMiddleware.cs # AES request body decryption
│       ├── Extensions/
│       │   └── PluginExtensions.cs         # DLL plugin loader
│       └── wwwroot/                        # Frontend static files (deployed here)
├── src/
│   └── OpenIddictUI.PluginDemo/           # Example plugin project
│       └── DemoPlugin.cs
└── tests/
    └── OpenIddictUI.Tests/
```

### 2.2 Technology Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 9 / ASP.NET Core |
| Authorization Server | OpenIddict 6.x (Abstractions + Core + Server + Validation + EF Core) |
| User Management | ASP.NET Core Identity |
| Database | PostgreSQL (primary), MySQL (secondary) |
| ORM | Entity Framework Core |
| Logging | Serilog |
| Frontend | Pure HTML + JS + CSS (zero-framework) |

## 3. OpenIddict Server Configuration

### 3.1 Flows

| Grant Type | OpenIddict Config | How It Works |
|------------|-------------------|--------------|
| `authorization_code` | `AllowAuthorizationCodeFlow()` | Browser redirect → login → consent → code → token |
| `password` | `AllowPasswordFlow()` | Direct POST to `/connect/token` with username+password |
| `phone_code` | Custom (application permission) | `POST /connect/token` with `grant_type=phone_code&phone_number=...&code=...` |
| `refresh_token` | `AllowRefreshTokenFlow()` | Standard refresh |

### 3.2 Endpoints

| Endpoint | OpenIddict Config | Handler |
|----------|-------------------|---------|
| `GET/POST /connect/authorize` | `SetAuthorizationEndpointUris()` + `EnableAuthorizationEndpointPassthrough()` | `AuthorizationController.Authorize()` |
| `POST /connect/token` | `SetTokenEndpointUris()` + `EnableTokenEndpointPassthrough()` | `AuthorizationController.Exchange()` |
| `GET/POST /connect/logout` | `SetLogoutEndpointUris()` + `EnableLogoutEndpointPassthrough()` | Built-in |

### 3.3 Program.cs Core Setup

```csharp
// EF Core + Identity
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseOpenIddict();
});

builder.Services.AddIdentity<User, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// OpenIddict
builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore().UseDbContext<AppDbContext>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("/connect/authorize")
               .SetTokenEndpointUris("/connect/token")
               .SetLogoutEndpointUris("/connect/logout");

        options.AllowAuthorizationCodeFlow()
               .AllowPasswordFlow()
               .AllowRefreshTokenFlow();

        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate();

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableLogoutEndpointPassthrough()
               .DisableTransportSecurityRequirement();
    })
    .AddValidation(options =>
    {
        options.UseLocalServer();
        options.UseAspNetCore();
    });
```

## 4. AuthorizationController (Core OpenIddict Logic)

### 4.1 Authorize (Authorization Code Flow)

```csharp
[HttpGet("~/connect/authorize")]
[HttpPost("~/connect/authorize")]
[IgnoreAntiforgeryToken]
public async Task<IActionResult> Authorize()
{
    var request = HttpContext.GetOpenIddictServerRequest()
        ?? throw new InvalidOperationException("OpenID Connect request not found.");

    // Authenticate from cookie
    var result = await HttpContext.AuthenticateAsync();
    if (!result.Succeeded)
    {
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = $"/index.html#/login?returnUrl={Uri.EscapeDataString(GetFullRequestUri())}"
        });
    }

    // Check consent
    var application = await _applicationManager.FindByClientIdAsync(request.ClientId);
    if (await RequiresConsentAsync(request, application))
    {
        return Redirect($"/index.html#/consent?returnUrl={Uri.EscapeDataString(GetFullRequestUri())}");
    }

    // Issue authorization code
    var principal = result.Principal;
    principal.SetScopes(request.GetScopes());
    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
```

### 4.2 Exchange (Token Endpoint)

```csharp
[HttpPost("~/connect/token")]
[Produces("application/json")]
public async Task<IActionResult> Exchange()
{
    var request = HttpContext.GetOpenIddictServerRequest()
        ?? throw new InvalidOperationException("OpenID Connect request not found.");

    // Password grant
    if (request.IsPasswordGrantType())
    {
        var user = await _userManager.FindByNameAsync(request.Username);
        if (user == null || !await _signInManager.CanSignInAsync(user))
            return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var passwordResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, true);
        if (!passwordResult.Succeeded)
            return Forbid(authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        return SignIn(await CreateUserPrincipal(user, request), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Phone code grant
    if (request.IsGrantType("phone_code"))
    {
        return await HandlePhoneCodeGrantAsync(request);
    }

    // Authorization code grant (handled automatically by OpenIddict)
    throw new InvalidOperationException("Unsupported grant type.");
}
```

## 5. AccountController (Frontend API Adapter)

### 5.1 Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| POST | `/account/login` | Password login via API. Validates credentials, signs in via ASP.NET Identity cookie, returns redirect URL. |
| POST | `/account/loginByCode` | SMS code login. Validates phone + code via `UserManager.VerifyUserTokenAsync()`, signs in, returns redirect. |
| POST | `/account/sendCode` | Sends SMS verification code. Rate-limited (60s). Requires captcha. |
| POST | `/account/resetPwd` | Reset password via phone code. |
| POST | `/account/resetPwdByOriginPwd` | Change password with old password. |
| GET | `/account/logout` | Show logout confirmation (redirects to `logout.html`). |
| POST | `/account/logout` | Execute logout, clear cookie, redirect to logged-out page. |

### 5.2 Login Flow (POST /account/login)

Frontend sends AES-encrypted `{username, password, captchaCode, rememberLogin, returnUrl, button}`.

```csharp
[HttpPost("Login")]
public async Task<IActionResult> Login([FromBody] LoginInput model)
{
    // Validate captcha
    if (!_captchaValidator.Validate(model.CaptchaCode))
        return ApiResult.Error(Errors.InvalidCaptcha, "验证码不正确");

    // Find user
    var user = await _userManager.FindByNameAsync(model.Username);
    if (user == null)
        return ApiResult.Error(Errors.InvalidCredentials, "用户名或密码错误");

    // Sign in with Identity cookie
    var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberLogin, true);
    if (!result.Succeeded)
    {
        return result.IsLockedOut ? ApiResult.Error(Errors.UserLockedOut, "用户被锁定")
             : result.IsNotAllowed ? ApiResult.Error(Errors.UserNotAllowed, "用户被禁用")
             : ApiResult.Error(Errors.InvalidCredentials, "用户名或密码错误");
    }

    // Return redirect URL (frontend navigates)
    return Ok(new { code = 200, location = model.ReturnUrl ?? "/", success = true });
}
```

## 6. Consent Management

### 6.1 Endpoints

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/consent?returnUrl=` | Returns consent page data (client name, logo, scopes) |
| POST | `/consent` | Processes consent decision (yes/no) |

### 6.2 GET /consent

```csharp
[HttpGet]
public async Task<IActionResult> Index(string returnUrl)
{
    var request = HttpContext.GetAuthorizationRequestAsync(returnUrl);
    var client = await _applicationManager.FindByClientIdAsync(request.ClientId);

    return Ok(new ApiResult
    {
        Data = new
        {
            clientName = await _applicationManager.GetDisplayNameAsync(client),
            clientLogoUrl = await _applicationManager.GetClientUriAsync(client),
            returnUrl,
            identityScopes = GetIdentityScopes(request.GetScopes()),
            resourceScopes = GetResourceScopes(request.GetScopes()),
            allowRememberConsent = true
        }
    });
}
```

## 7. Phone Code Grant

Custom OAuth 2.0 extension grant type `phone_code` for token endpoint.

```csharp
// Application permission:
OpenIddictConstants.Permissions.Prefixes.GrantType + "phone_code"

// Token request:
POST /connect/token
Content-Type: application/x-www-form-urlencoded

grant_type=phone_code&phone_number=13800138000&code=123456&client_id=socodb-web&client_secret=xxx

// Handler in AuthorizationController.Exchange():
async Task<IActionResult> HandlePhoneCodeGrantAsync(OpenIddictRequest request)
{
    var phone = (string)request["phone_number"];
    var code = (string)request["code"];

    var user = await _userManager.FindByPhoneAsync(phone);
    if (user == null)
        return ForbidInvalidGrant("用户不存在");

    var isValid = await _userManager.VerifyUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, "Login", code);
    if (!isValid)
        return ForbidInvalidGrant("验证码不正确");

    var principal = await CreateUserPrincipal(user, request);
    principal.SetScopes(request.GetScopes());
    return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
}
```

## 8. SMS Verification Code

### 8.1 Send Code (POST /account/sendCode)

```csharp
[HttpPost("SendCode")]
public async Task<ApiResult> SendCode([FromBody] SendCodeInput model)
{
    // Rate limit: 60s per phone
    if (await _rateLimiter.IsLimitedAsync(model.PhoneNumber))
        return ApiResult.Success("发送成功"); // 模糊响应防爆破

    // Validate captcha
    if (!_captchaValidator.Validate(model.CaptchaCode))
        return ApiResult.Error(Errors.InvalidCaptcha, "验证码不正确");

    // Generate token and send SMS
    var code = await _userManager.GenerateUserTokenAsync(user, TokenOptions.DefaultPhoneProvider, model.Scenario);
    await _smsSender.SendAsync(phoneNumber, code);

    return ApiResult.Success("发送成功");
}
```

Scenarios: `Login`, `ResetPassword`, `Register`.

## 9. Captcha

```csharp
[Route("api/v1.0/captcha")]
public class CaptchaController : ControllerBase
{
    [HttpGet("generate")]
    public IActionResult Generate()
    {
        var code = CaptchaGenerator.GenerateCode(4);
        HttpContext.Session.SetString("CaptchaCode", code);
        var image = CaptchaGenerator.GenerateImage(code);
        return File(image, "image/png");
    }
}
```

## 10. Session Management

```csharp
[Route("session")]
[Authorize]
public class SessionController : ControllerBase
{
    [HttpGet]
    public IActionResult GetInfo()
    {
        return Ok(new ApiResult
        {
            Code = 200,
            Data = User.Claims
                .Where(c => c.Type != "AspNet.Identity.SecurityStamp")
                .Select(c => new { type = c.Type, value = c.Value })
        });
    }
}
```

## 11. Plugin System

DLL-based plugin loading (mirrors existing STS mechanism):

```csharp
public static class PluginExtensions
{
    public static void LoadPlugins(this IHostApplicationBuilder builder)
    {
        if (!Directory.Exists("Plugins")) return;

        foreach (var dll in Directory.GetFiles("Plugins", "*.dll"))
        {
            var assembly = Assembly.LoadFrom(dll);
            var pluginTypes = assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Plugin"));

            foreach (var type in pluginTypes)
            {
                type.GetMethod("Load")?.Invoke(null, new object[] { builder });
            }
        }
    }

    public static void UsePlugins(this WebApplication app)
    {
        // Similar: invoke Use() on each plugin type
    }
}
```

### Plugin Contract

```csharp
public static class MyPlugin
{
    public static void Load(IHostApplicationBuilder builder)
    {
        // Register services, controllers, grant validators
        builder.Services.AddTransient<ISomeService, MyImplementation>();
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(MyPlugin).Assembly);
    }

    public static void Use(WebApplication app)
    {
        // Register middleware, endpoints
    }
}
```

## 12. AES Request Decryption (Middleware)

Frontend encrypts POST bodies with AES-ECB-PKCS7. Backend decrypts via middleware:

```csharp
public class DecryptRequestMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var version = context.Request.Headers["Z-Encrypt-Version"];
        if (version == "v1.1" && context.Request.Method == "POST")
        {
            var encryptedKey = context.Request.Headers["Z-Encrypt-Key"];
            var realKey = GetRealKey(encryptedKey); // extract original UUID

            using var reader = new StreamReader(context.Request.Body);
            var encryptedBody = await reader.ReadToEndAsync();

            var decrypted = AesEcbDecrypt(encryptedBody, realKey);
            // Replace request body with decrypted JSON
        }
        await _next(context);
    }
}
```

## 13. Response Format

All API responses use the existing convention:

```json
// Success
{ "code": 200, "success": true, "message": "成功", "data": {...} }

// Success with redirect
{ "code": 200, "location": "/connect/authorize?client_id=...", "success": true }

// Error
{ "code": 4004, "success": false, "message": "用户名或密码错误" }
```

Error codes maintained from existing STS for frontend compatibility.

## 14. Database Migrations

```bash
# PostgreSQL
dotnet ef migrations add InitialCreate --context AppDbContext
dotnet ef database update --context AppDbContext
```

OpenIddict tables (`OpenIddictApplications`, `OpenIddictAuthorizations`, `OpenIddictScopes`, `OpenIddictTokens`) are created automatically via `UseOpenIddict()`.

## 15. Out of Scope

- User registration (separate feature)
- Admin/dashboard
- i18n (CN only for MVP)
- Grant management page (separate)
- Production certificate management (dev certs for MVP)
