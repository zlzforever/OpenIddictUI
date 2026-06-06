# Grants — AGENTS.md

**Generated:** 2026-06-02

## OVERVIEW
`IGrantHandler` interface + `BaseGrantHandler` base class for pluggable OAuth token endpoint grant types.

## STRUCTURE
```
Grants/
├── IGrantHandler.cs           # Interface: GrantType + HandleAsync
├── BaseGrantHandler.cs        # Base: Success/Failure/GetDestinations/ExecuteAsync
├── GrantResult.cs             # Return: Principal | Error
├── PasswordGrantHandler.cs    # grant_type=password
├── PhoneCodeGrantHandler.cs   # grant_type=phone_code
└── AuthorizationCodeGrantHandler.cs  # code + refresh_token
```

## CONVENTIONS
- **No constructor DI** — resolve deps from `context.RequestServices` at runtime (supports any lifetime)
- Return `GrantResult` (Success/Failure), `BaseGrantHandler.ExecuteAsync()` converts to `IActionResult`
- Plugin registration: `builder.Services.AddSingleton<IGrantHandler, MyHandler>()`
- `ForbidResult` + `SignInResult` handled in base class; subclasses only call `Success(principal)` / `Failure(desc)`
- Resolution: handler selected by `grant_type` via `GetKeyedService<IGrantHandler>(grantType)` from DI keyed service

## NOTES
- `GetDestinations` returns `IList<string>` (OpenIddict 7.x requirement)
- `SignInResult` must use `Microsoft.AspNetCore.Mvc.SignInResult` (avoids conflict with Identity's `SignInResult`)
