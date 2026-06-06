# Controllers — AGENTS.md

**Generated:** 2026-06-02

## OVERVIEW
OAuth protocol endpoints + login APIs + utility controllers. All return unified `ApiResult` format.

## WHERE TO LOOK
| Controller | Role |
|------------|------|
| `AuthorizationController.cs` | `/connect/authorize` (authorize + consent redirect), `/connect/token` (delegates to GrantHandler), `/connect/logout` (RP-Initiated) |
| `AccountController.cs` | `/account/login`, `/account/loginByCode`, `/account/sendCode`, `/account/logout`, `/account/resetPwd*` |
| `ConsentController.cs` | `/api/consent/{id}` — GET data + POST create permanent grant |
| `SessionController.cs` | `/session` — return current user claims + consented clients |
| `CaptchaController.cs` | `/api/v1.0/captcha` — SkiaSharp image captcha |
| `AntiforgeryController.cs` | `/api/antiforgery/token` — get XSRF token |
| `Input/` | Request DTOs (LoginInput, SendCodeInput, etc.) |
| `Extensions.cs` | Controller extension helpers |
| `ApiResult.cs` | Unified API response type |

## CONVENTIONS
- `[Authorize]` → `AuthenticateAsync()` with no scheme (default = cookie)
- Manual CSRF: `if (!await ValidateCsrf()) return CsrfError()`
- Soft delete: `UserManager` queries auto-filter via EF Core Global Filter (`AppDbContext`)
- Dev captcha skip: `env.IsDevelopment()` in `CheckCaptcha()`

## ANTI-PATTERNS
- Don't throw → return `Forbid()` + `AuthenticationProperties`
- Don't call `GetOpenIddictServerRequest()` in non-passthrough endpoints
- Don't pass `returnUrl` to frontend for consent → use `HybridCache` + `ConsentEntry`, pass only id
