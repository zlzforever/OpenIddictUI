# OpenIddictUI Login Interface Design

## 1. Overview

Build a pure HTML + JavaScript login UI for an OpenIddict-based (IdentityServer4) Security Token Service. The UI replaces the existing jQuery+Bootstrap3 frontend while maintaining full compatibility with the backend API, encryption layer, and plugin system.

**Key objectives:**
- Minimal dependencies — vanilla JS + CSS; crypto-js (already in existing STS) as the sole external library for AES-ECB encryption
- Multi-tab login: password, SMS verification code, and third-party external providers
- Plugin-injectable third-party login via a JavaScript hook API
- AES request body encryption compatible with existing backend middleware
- SPA architecture for smooth user experience

**Backend reference:** `SecurityTokenService` (uses IdentityServer4, not OpenIddict — naming is historical).

## 2. Architecture

### 2.1 SPA with Hash Routing

Single `index.html` entry point. Views are dynamically rendered based on hash fragments. No page reloads between login tabs or consent/logout flows.

```
index.html
├── #/login         → Login form (3 tabs: Password, SMS, External Providers)
├── #/consent       → OAuth scope consent page
├── #/logout        → Logout confirmation
├── #/logged-out    → Post-logout redirect page
├── #/error         → Error display by error code
```

### 2.2 File Structure

```
OpenIddictUI/
├── index.html
├── css/
│   └── styles.css
├── js/
│   ├── app.js                 # Entry: router init, view mounting, plugin bootstrap
│   ├── router.js              # Lightweight hash-based router (~60 lines)
│   ├── http.js                # HTTP client with AES-ECB encryption
│   ├── captcha.js             # Captcha image loading and refresh
│   ├── session.js             # Session state (check /session endpoint)
│   ├── providers.js           # Third-party provider registry (plugin hook API)
│   └── views/
│       ├── login-form.js      # Password-based login view
│       ├── sms-login.js       # SMS verification code login view
│       ├── external-buttons.js # External provider button rendering
│       ├── consent.js         # OAuth consent view
│       ├── logout.js          # Logout confirmation view
│       ├── logged-out.js      # Post-logout view
│       └── error.js           # Error display view
└── assets/
```

### 2.3 Component Responsibilities

| Module | Responsibility |
|--------|---------------|
| `router.js` | Parse hash, map to view functions, manage view transitions |
| `http.js` | AES encrypt POST bodies, add Z-Encrypt headers, parse responses |
| `captcha.js` | Load captcha image from `/api/v1.0/captcha/generate`, refresh on click |
| `session.js` | Check `/session` for authenticated user, render user info in navbar |
| `providers.js` | Maintain provider registry, expose `registerExternalProvider()` API, fetch provider list from backend when available |
| `views/login-form.js` | Render password form, validate inputs, POST to `/account/login`, handle redirect |
| `views/sms-login.js` | Render SMS login form, handle "Send Code" button with 60s cooldown, POST to `/account/loginByCode` |
| `views/external-buttons.js` | Render registered external provider buttons, handle click-to-redirect |
| `views/consent.js` | GET `/consent?returnUrl=`, render scopes, POST `/consent` with user choices |
| `views/logout.js` | GET logoutId from query, render confirmation, POST `/account/logout` |
| `views/logged-out.js` | Parse query params, redirect or show post-logout link |
| `views/error.js` | Map error codes to user-facing messages |

## 3. Data Flow

### 3.1 AES Encryption (Request Body)

Every POST request body is encrypted using AES-ECB-PKCS7 via `crypto-js` (the Web Crypto API does not support ECB mode, so crypto-js is the single accepted external dependency — it is already used in the existing STS project).

1. Generate a UUID as the AES key
2. Insert a 6-char random string at position 10 of the key → `Z-Encrypt-Key` header
3. Set `Z-Encrypt-Version: v1.1` header
4. Encrypt `JSON.stringify(payload)` with the original UUID key (before insertion)
5. Send Base64-encoded ciphertext as the request body
6. Set `Content-Type: application/json`

### 3.2 API Endpoints Used

| Method | Endpoint | Input | Response |
|--------|----------|-------|----------|
| POST | `/account/login` | `{username, password, captchaCode, rememberLogin, returnUrl, button}` | `{location}` or `{code, message}` |
| POST | `/account/loginByCode` | `{phoneNumber, verifyCode, rememberLogin, returnUrl, button}` | `{location}` or `{code, message}` |
| POST | `/account/sendCode` | `{phoneNumber, countryCode, scenario, captchaCode}` | `{code, message}` |
| POST | `/account/logout` | `{logoutId}` | Redirect |
| GET | `/consent` | `?returnUrl=` | `{data: {clientName, scopes, ...}}` |
| POST | `/consent` | `{button, scopesConsented, rememberConsent, returnUrl}` | Redirect |
| GET | `/session` | — | `{data: [{type, value}]}` |
| GET | `/api/v1.0/captcha/generate` | `?_t=` | Image binary |

### 3.3 Error Code Mapping

| Code | Message |
|------|---------|
| 4001 | 不支持双因素认证 |
| 4002 | 用户被禁止登录 |
| 4003 | 用户被锁定 |
| 4004 | 用户名或密码不正确 |
| 4005 | 不支持 NativeClient |
| 4006 | 返回地址不合法 |
| 4007 | 选择的操作不正确 |
| 4008 | 没有 Scope 可匹配 |
| 4009 | 客户端标识出错 |
| 4010 | 授权请求链接不正确 |
| 4011 | 登录失败 |
| 4012 | 用户不存在 |
| 4013 | 验证码过期 |
| 4014 | 验证码不正确 |
| 4015 | 密码不符合安全要求 |
| 4016 | 修改密码失败 |
| 4017 | 短信发送失败 |

### 3.4 View State Flow

```
#/login  ──success──► window.location = response.location
  │
  └──needs consent──► #/consent ──success──► window.location = response.location (or redirect.html)
  │
  └──logout──► #/logout ──confirm──► POST /account/logout ──► #/logged-out
  │                                           │
  └──error──► #/error?errorId=N               └──► redirect to postLogoutRedirectUri
```

## 4. Plugin System: Third-Party Login

### 4.1 Registration API

Third-party developers register providers via `window.OpenIddictUI.registerExternalProvider()`:

```js
OpenIddictUI.registerExternalProvider({
  id: 'github',
  name: 'GitHub',
  icon: '<svg>...</svg>',   // inline SVG or URL string
  color: '#24292e',          // button accent color
  handler: () => {
    // Redirect user to the external OAuth endpoint
    window.location.href = '/external/github';
  }
});
```

### 4.2 Backend-Driven Providers (Future)

The `providers.js` module can optionally call `GET /api/external-providers` (endpoint to be added by backend plugins) to fetch provider metadata dynamically. This allows backend plugins to auto-register providers without custom JS.

### 4.3 Plugin Integration Flow

```
Backend plugin DLL                    Frontend
└── Load(): add /external/github      providers.js
    endpoint + controller         ┌── registerExternalProvider({id:'github', ...})
                                  │
                                  ▼
                              external-buttons.js
                              └── renders buttons for all registered providers
```

For the initial implementation, plugins deliver a small JS file loaded via `<script>` tag that calls `registerExternalProvider()`.

## 5. UI Design

### 5.1 Login Page Layout

```
┌──────────────────────────────────────────────┐
│  [SecurityTokenService]               [User] │  ← Navbar
├──────────────────────────────────────────────┤
│                                              │
│                  Login                       │
│              Choose how to login             │
│                                              │
│   ┌─────────┬──────────┬──────────────┐     │
│   │ Account │   SMS    │  Third-party │     │  ← Tabs
│   └─────────┴──────────┴──────────────┘     │
│                                              │
│   ┌─ Tab Content ──────────────────────────┐ │
│   │                                         │ │
│   │  Username:  [________________]          │ │
│   │  Password:  [________________]          │ │
│   │  Captcha:   [______]  [captcha-img]     │ │
│   │  □ Remember Me                          │ │
│   │  [Login] [Cancel]                       │ │
│   └─────────────────────────────────────────┘ │
│                                              │
│   ██████████ Error message area ██████████   │
└──────────────────────────────────────────────┘
```

### 5.2 CSS Strategy

- Pure CSS, no framework
- CSS custom properties for theming (`--primary-color`, `--error-color`, etc.)
- Responsive: single-column on mobile, centered card on desktop
- Subtle transitions for tab switching, button hover
- Accessible: proper contrast ratios, focus indicators, `aria-*` attributes

### 5.3 Third-Party Button Style

Each provider button renders with the provider's brand color, an icon, and the name:

```html
<button class="external-provider-btn" style="--provider-color: #24292e">
  <span class="provider-icon"><svg>...</svg></span>
  Continue with GitHub
</button>
```

## 6. Error Handling

- **Network errors:** Display generic "服务器出小差" message
- **Captcha expiry:** Auto-refresh on error 4013, prompt re-entry
- **Form validation:** Client-side checks before submission (username 1-24 chars, password 1-24 chars, phone 11+ digits)
- **Rate limiting:** 60-second cooldown on SMS send button (local, mirrors server-side 60s limit)
- **Iframe escape:** Login success redirects break out of iframes using `window.top`

## 7. Testing Strategy

- Each JS module tested independently with mock DOM and mock fetch
- Integration tests for the full login flow (happy path + error paths)
- AES encryption module tested against known test vectors from the backend
- Manual verification against all error codes

## 8. Non-Goals (Out of Scope)

- Password change/reset UI (separate feature)
- User registration (separate feature)
- Grant management page
- Admin/dashboard functionality
- i18n (English only for MVP)
- Dark mode (can be added later via CSS custom properties)
