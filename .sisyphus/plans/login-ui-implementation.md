# Implementation Plan: OpenIddictUI Login Interface

**Spec:** `docs/superpowers/specs/2026-05-26-login-ui-design.md`  
**Target:** Pure HTML + JS login UI for IdentityServer4-based STS  
**Architecture:** SPA with hash routing, vanilla JS + CSS, crypto-js for AES

---

## Phase 1: Foundation (Sequential — phase 2 depends on this)

### 1.1 Project Scaffold
- Create directory structure: `css/`, `js/`, `js/views/`, `assets/`
- Create `index.html` shell with meta tags, favicon, `<div id="app">`, script imports
- CSS reset and base styles (`:root` variables, body, navbar, container)
- Expected: Empty page with navbar that loads without errors

### 1.2 Core Modules (can be parallelized)
- **`js/router.js`**: Hash-based router — parse `location.hash`, map to view functions, `navigateTo(hash)` API
- **`js/http.js`**: `post(url, data)` and `get(url)` wrappers; AES-ECB encryption via crypto-js; `Z-Encrypt-Key`/`Z-Encrypt-Version` headers; `withCredentials: true`
- **`js/session.js`**: `getSession()` → call `/session`, return user info or null; `isAuthenticated()` helper
- **`js/captcha.js`**: `loadCaptcha(imgElement)` → set `src` to `/api/v1.0/captcha/generate?_t=timestamp`; refresh on click; auto-refresh after error codes 4013
- **`js/providers.js`**: `OpenIddictUI.registerExternalProvider(config)` registry; `getProviders()` returns list; event system for provider registration

### 1.3 Entry Point
- **`js/app.js`**: Initialize router, map hash patterns to view functions, load session, mount navbar user info, bootstrap providers

**Verification:** Each module can be tested independently. `http.js` can be verified against known AES test vectors from the backend.

---

## Phase 2: Views (ALL parallel — independent views)

Each view is a self-contained module that:
1. Exports a `render(container)` function
2. Sets up its own DOM event listeners
3. Uses `http.js` for API calls
4. Handles its own error display within its container

### 2.1 Login Form (`js/views/login-form.js`)
- Render: username input, password input, captcha input + image, "Remember Me" checkbox, Login/Cancel buttons
- Validate: username required (1-24 chars), password required (1-24 chars), captcha required
- POST `/account/login` with encrypted body
- On success: `window.location = response.location` (with iframe breakout)
- On error: show error message using error code mapping
- Dependencies: `http.js`, `captcha.js`

### 2.2 SMS Login (`js/views/sms-login.js`)
- Render: phone number input, captcha input + image, "Send SMS" button (with 60s countdown lock), verify code input, Login/Cancel buttons
- "Send SMS" → POST `/account/sendCode` with `{phoneNumber, countryCode:"+86", scenario:"Login", captchaCode}`
- Login → POST `/account/loginByCode` with `{phoneNumber, verifyCode, returnUrl, button:"login"}`
- Dependencies: `http.js`, `captcha.js`

### 2.3 External Provider Buttons (`js/views/external-buttons.js`)
- Render: list of registered external provider buttons from `providers.js`
- Each button styled with provider's `color` and `icon`
- Click → call provider's `handler()` function
- Show "No external providers configured" if registry is empty (graceful)
- Dependencies: `providers.js`

### 2.4 Consent Page (`js/views/consent.js`)
- GET `/consent?returnUrl=` → render client name, logo, identity/resource scopes as checkboxes
- POST `/consent` with `{button, scopesConsented, rememberConsent, returnUrl}`
- On success: redirect to `response.location` or `redirect.html?redirectUrl=...`
- Dependencies: `http.js`

### 2.5 Logout (`js/views/logout.js`)
- Parse `logoutId` from URL query params
- Render confirmation with hidden `logoutId` field
- POST `/account/logout` with form data
- Redirect to the response URL
- Dependencies: `http.js`

### 2.6 Logged Out (`js/views/logged-out.js`)
- Parse query params: `postLogoutRedirectUri`, `clientName`, `signOutIframeUrl`, `automaticRedirectAfterSignOut`
- Show "You are now logged out" + client name + return link
- Auto-redirect if `automaticRedirectAfterSignOut` is true
- Render sign-out iframe if present
- No API calls — display only

### 2.7 Error Page (`js/views/error.js`)
- Parse `errorId` from query params
- Map to user-facing message using the error code table
- Display message in a styled error container
- No API calls — display only

**Verification:** Each view renders correctly in isolation when loaded by router.

---

## Phase 3: Styles (`css/styles.css`)

### 3.1 Layout & Components
- CSS custom properties: `--primary`, `--error`, `--bg`, `--text`, `--border`, `--radius`
- Navbar: dark top bar with brand link and user info (right-aligned)
- Login card: centered, max-width 420px, shadow, padding
- Tabs: horizontal tab bar with active state indicator, smooth transition
- Form elements: inputs, buttons, checkboxes — consistent sizing, focus states, error states
- Captcha image: bordered, cursor pointer, hover effect
- External provider buttons: full-width, brand color accent, icon on left
- Error message: red alert box with icon
- Responsive: single column on mobile, centered card on desktop (media query at 768px)

### 3.2 States
- Loading: button spinner or disabled state during API calls
- Error: red border on inputs, error message display
- Success: subtle green flash or transition
- Disabled: grayed out buttons during SMS cooldown

---

## Phase 4: Plugin Demo

### 4.1 Example Provider
- Create a minimal example script that uses `registerExternalProvider()` to add a "Demo Login" button
- Document the plugin API inline

---

## Phase 5: Integration & Verification

### 5.1 Full Flow Test
- Navigate to `index.html#/login?returnUrl=/connect/authorize?...`
- Test password login (happy path + error codes 4004, 4003, 4002)
- Test SMS login flow (send code + login)
- Test consent flow
- Test logout flow
- Verify captcha refresh works
- Verify AES encryption produces correct headers

### 5.2 Cross-browser
- Verify in latest Chrome, Firefox, Safari
- No IE11 support required (uses modern JS features)

---

## Execution Strategy

**Phase 1 can be parallelized** (all core modules are independent):
- `router.js`, `http.js`, `session.js`, `captcha.js`, `providers.js` → 5 parallel tasks

**Phase 2 can be fully parallelized** (all 7 views are independent):
- `login-form.js`, `sms-login.js`, `external-buttons.js`, `consent.js`, `logout.js`, `logged-out.js`, `error.js` → 7 parallel tasks

**Phase 3** depends on views existing but doesn't need them to be perfect — can start in parallel with Phase 2.

**Phase 4** is a small demo, can run anytime after Phase 1.

**Phase 5** requires everything to be done first.
