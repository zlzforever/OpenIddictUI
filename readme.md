# OpenIddictUI

基于 .NET 10 + OpenIddict 7.x + Vue 3 的 OpenID Connect 认证服务（STS）。

## 快速启动

```bash
# STS (Security Token Service) — http://localhost:5164
dotnet run --project src/OpenIddictUI --urls http://localhost:5164

# 受保护 API — http://localhost:5100
dotnet run --project src/OpenIddictUI.Api --urls http://localhost:5100

# STS 前端构建（Vue 3 SPA → wwwroot）
cd frontend && npm run build

# SPA Demo 客户端 — http://localhost:5175
cd client-spa && npm run dev -- --port 5175
```

## 项目结构

```
OpenIddictUI/
├── src/OpenIddictUI/        # STS 后端 (OpenIddict server + ASP.NET Core Identity)
├── src/OpenIddictUI.Api/    # 受保护 WebAPI (JwtBearer 验证)
├── frontend/                # STS 管理前端 (Vue 3 SPA, 登录/consent/退出)
└── client-spa/              # 示例 SPA 客户端 (oidc-client-ts, 演示 OIDC 接入)
```

## 架构总览

```
┌─────────────────────┐    OAuth 2.0     ┌──────────────────────┐
│   client-spa (5175) │ ◄──────────────► │   STS (5164)         │
│   oidc-client-ts    │    code/token    │   OpenIddict 7.x     │
│   (外部 OIDC 客户端) │                  │   + Identity         │
└──────────┬──────────┘                  └──────────┬───────────┘
           │                                        │
           │  access_token                          │ 签发 JWT
           ▼                                        ▼
┌─────────────────────┐              ┌──────────────────────┐
│   API (5100)         │              │   STS Frontend       │
│   JwtBearer 验证     │              │   Vue 3 SPA          │
│   scope: api1        │              │   登录/consent/管理   │
└─────────────────────┘              └──────────────────────┘
```

## 完整认证流程

### 流程一：STS 前端用户登录

STS 自身管理前端的登录（非 OAuth client 流程）。

```
1. 用户访问 STS 前端 (localhost:5164)
   → Vue Router beforeEach() 拦截
   → GET /session 检查是否已登录
      ├─ 已登录 → 跳转 /welcome
      └─ 未登录 → 显示 /account/login 页

2. 登录页支持三种方式：

   ┌── 密码登录
   │   POST /account/login
   │   { username, password, captchaCode, rememberLogin, button:"login", returnUrl }
   │   → AccountController.Login()
   │     ① ValidateCsrf() — 手动校验 X-XSRF-TOKEN header
   │     ② returnUrl 白名单校验 — 只能以 /connect/authorize? 开头
   │     ③ CheckCaptcha() — 从 Session["CaptchaCode"] 读取对比，Dev 环境跳过
   │     ④ ForcePasswordSecurityPolicy → 验证密码强度
   │     ⑤ UserManager.FindByNameAsync() → 查找用户
   │     ⑥ SignInManager.PasswordSignInAsync(user, pwd, rememberMe, lockoutOnFailure=true)
   │        锁定机制：5 次连续失败 → 3 分钟锁定
   │     ⑦ 成功 → 写入 idsrv cookie (HttpOnly, SameSite=Lax, 120h 滑动过期)
   │        → 返回 { location: returnUrl }
   │
   ├── 短信验证码登录
   │   ① POST /account/sendCode { phoneNumber, captchaCode, scenario:"Login" }
   │      限频：同一手机号 60s 内不发第二次 (HybridCache)
   │      UserManager.GenerateUserTokenAsync(DefaultPhoneProvider, "Login") → 6 位验证码
   │      → 阿里云短信发送
   │   ② POST /account/loginByCode { phoneNumber, verifyCode, returnUrl }
   │      CSRF 校验 → UserManager.VerifyUserTokenAsync(...) → 成功/失败
   │      失败 → 累加锁定计数 (防暴力破解)
   │      成功 → SignInManager.SignInAsync() + UpdateSecurityStampAsync()
   │
   └── 第三方登录
       依赖外部 Identity Provider

3. 登录成功 → 前端 window.location.href = returnUrl
```

### 流程二：OAuth 2.0 Authorization Code Flow

外部 SPA 客户端获取 token 的标准 OIDC 流程。

```
Step 1: 客户端发起登录
  userManager.signinRedirect() (oidc-client-ts)
  → 浏览器重定向到:
    GET /connect/authorize?
        client_id=spa-client
        &redirect_uri=http://localhost:5175/signin-redirect-callback
        &response_type=code
        &scope=openid profile api1
        &code_challenge=xxx           ← PKCE S256
        &code_challenge_method=S256

Step 2: OpenIddict 内置中间件校验 (Passthrough 前置)
  → client_id 注册检查 ✓
  → redirect_uri 精确匹配 ✓
  → scope 已注册 ✓
  → PKCE code_challenge 存在 (RequirePkce=true) ✓
  → 校验通过 → 放行给 AuthorizationController.Authorize()

Step 3: AuthorizationController.Authorize()
  ① 再次校验 client_id + 启用状态 (Settings["enabled"])
  ② HttpContext.AuthenticateAsync() — 检查 idsrv cookie
     ├─ 未登录 + prompt=none → 返回 login_required
     ├─ 未登录 → Challenge() → 302 跳转 /account/login?returnUrl=...
     │  → 用户完成登录后回到此处
     └─ 已登录 → 继续
  ③ 检查 max_age → 超时则重新认证
  ④ prompt=login → SignOut + 强制重新登录
  ⑤ 检查 Consent 类型 (ConsentType):
     ├─ implicit/external → 无需用户手动同意，直接签发
     └─ explicit → 检查是否有永久授权记录
         ├─ 有 → 直接签发
         └─ 无 → ConsentEntry 存入 HybridCache (10min TTL)
               → 302 跳转 /consent/{randomId}
  ⑥ 签发 authorization_code:
     → SignInManager.CreateUserPrincipalAsync(user)
     → SetClaim(Subject) + SetScopes + SetResources
     → SetDestinations() — 决定每个 claim 出现在 AccessToken/IdentityToken/两者
     → OpenIddict 自动生成 authorization_code
     → 302 跳转回客户端 redirect_uri?code=xxx

Step 4: Consent 流程 (如需要)
  GET /api/consent/{id} → 返回客户端信息 + scope 列表
  用户点同意 → POST /api/consent/{id} { button:"yes", scopesConsented:[...] }
    ① 从 HybridCache 再次读取 ConsentEntry
    ② 校验 scopesConsented ⊆ 原始请求 scope (防前端篡改扩权)
    ③ authorizationManager.CreateAsync() → 创建永久授权 (Permanent)
    ④ 返回 { location: entry.ReturnUrl } → 前端跳回 authorize → 签发 code

Step 5: 用 authorization_code 兑换 token
  POST /connect/token
    grant_type=authorization_code
    &code=xxx
    &code_verifier=xxx          ← PKCE 验证
    &redirect_uri=...
    &client_id=spa-client

  → AuthorizationController.Exchange()
    → GetKeyedService<IGrantHandler>("authorization_code")
    → AuthorizationCodeGrantHandler:
      → AuthenticateAsync(OpenIddictServerAspNetCoreDefaults)
      → OpenIddict 自动校验 PKCE (S256)
      → 成功 → 返回 access_token + id_token + refresh_token

Step 6: 客户端存储 token
  → oidc-client-ts → WebStorageStateStore (localStorage)
  → 后续 API 请求: Authorization: Bearer <access_token>
```

### 流程三：Password Grant (用户名密码直接换 token)

```
POST /connect/token
  grant_type=password
  &username=xxx
  &password=xxx
  &scope=api1

→ PasswordGrantHandler:
  ① FindByNameAsync(username)
  ② CheckPasswordSignInAsync(user, password, lockoutOnFailure=true)
  ③ 成功 → CreateUserPrincipalAsync → SetScopes → SetDestinations
  ④ OpenIddict 签发 access_token + refresh_token
```

### 流程四：Phone Code Grant (自定义 grant)

```
POST /connect/token
  grant_type=phone_code
  &phone_number=xxx
  &code=123456
  &scope=api1

→ PhoneCodeGrantHandler:
  ① 按 phone_number 查找用户
  ② VerifyUserTokenAsync(DefaultPhoneProvider, "Login", code)
  ③ 成功 → 签发 access_token，设置 amr=phone_code
```

### 流程五：Refresh Token 轮转

```
POST /connect/token
  grant_type=refresh_token
  &refresh_token=xxx

→ AuthorizationCodeGrantHandler (与 authorization_code 共用)
  → OpenIddict 自动验证 refresh_token + 轮转
  → 返回新的 access_token + 新的 refresh_token (旧 refresh_token 失效)
```

### 流程六：登出

```
┌── STS 前端登出
│   POST /account/logout { CSRF 校验 }
│   → SignInManager.SignOutAsync() — 清除 idsrv cookie
│   → 返回 { location: "/logged-out" }
│
├── OIDC RP-Initiated Logout
│   GET /connect/logout
│   → SignOutAsync(ApplicationScheme) + SignOut(OpenIddict scheme)
│   → OpenIddict 根据 post_logout_redirect_uri 重定向
│
└── 外部 SPA 登出
    userManager.signoutRedirect()
    → 清除 localStorage 中的 tokens
    → 重定向到 STS 登出
```

## 中间件管线

顺序敏感，来自 `Program.cs`：

```
DecryptRequestMiddleware  (解密加密请求体)
  → HealthChecks (/healthz)
  → CookiePolicy
  → DefaultFiles
  → StaticFiles
  → Routing
  → Antiforgery            (XSRF cookie + header)
  → CORS ("cors")
  → Session
  → Authentication         (cookie + openiddict)
  → Authorization
  → Controllers
  → Fallback (index.html)
```

## 关键配置

| 配置项 | 值 | 位置 |
|--------|-----|------|
| Cookie 名 | `idsrv` | `appsettings.json` → `ApplicationCookieAuthentication` |
| Cookie 过期 | 120h 滑动 | 同上 |
| Session 空闲超时 | 10min | `Program.cs` → `AddSession` |
| 密码锁定 | 5次 → 3分钟 | `appsettings.json` → `Identity.Lockout` |
| 密码策略 | 8位+大小写+数字+特殊字符 | `appsettings.json` → `Identity.Password` |
| PKCE | 强制 S256 | `openiddict-seed.json` → `spa-client.RequirePkce` |
| Consent | explicit | `openiddict-seed.json` → `spa-client.ConsentType` |
| Signing Key | RSA 2048, 自签名, 100年 | `OpenIddictServierBuilderExtensions.cs` |
| Access Token | 不加密 (jwt) | `Program.cs` → `DisableAccessTokenEncryption()` |

## 重要说明

### 客户端类型 (ClientType)

OpenIddict 区分三种客户端类型，配置在 `openiddict-seed.json` 的 `ClientType` 字段：

| 类型 | 适用场景 | client_secret | token 端点认证方式 |
|------|---------|---------------|-------------------|
| `public` | SPA (Vue/React/Angular)、移动 App | 不用填 | PKCE code_verifier |
| `confidential` | 后端服务、Server-side 应用 | 必须填 | client_secret 或 JWT assertion |
| `hybrid` | 前后端混合（不推荐） | 必须填 | client_secret + PKCE |

**常见错误**：SPA 客户端（如 `spa-client`）必须设为 `public`，否则 OpenIddict 会要求 client_secret，浏览器无法安全存储。控制面板会报 `ID2198` 错误。IdentityServer4 的 Implicit Flow 没有这个问题，但 OAuth 2.1 已废弃 Implicit，当前项目使用 Authorization Code + PKCE，SPA 到 token 端点的 `POST /connect/token` 调用是正常的标准流程。

### 种子数据 (Seed Data)

客户端和 scope 配置在 `openiddict-seed.json`，应用启动时由 `SeedData.ApplyAsync()` 自动创建或跳过（已存在的不会更新）。**修改 seed JSON 后需手动删除旧记录才能生效**：

```sql
DELETE FROM openiddict.openiddict_applications WHERE client_id = 'spa-client';
DELETE FROM openiddict.openiddict_scopes WHERE name = 'api1';
```

### ConsentType（授权同意方式）

| 值 | 行为 |
|----|------|
| `implicit` | 不显示同意页，直接签发 code（适用于信任客户端） |
| `explicit` | 首次授权时弹出同意页，用户确认后创建永久授权记录，后续自动跳过 |
| `external` | 由外部系统管理同意 |

`spa-client` 使用 `explicit`，如果需要跳过同意页（调试阶段），可改为 `implicit`。

### 中间件管线顺序（务遵守）

`Program.cs` 中的中间件注册有严格顺序依赖，特别是 Antiforgery 必须在 CORS 和 Session 之前：

```
DecryptRequest → HealthChecks → CookiePolicy → DefaultFiles → StaticFiles
→ Routing → Antiforgery → CORS → Session → Auth → Controllers → Fallback
```

**常见错误**：Antiforgery 放在 Session 后面会导致 XSRF cookie 写入失败。

### OpenIddict Passthrough 模式

请求先经过 OpenIddict 内置中间件做协议层校验（redirect_uri、scope、PKCE 等），校验通过后"放行"给 Controller 处理业务逻辑。若校验失败，OpenIddict 自动返回 OAuth error，不进入 Controller。这意味着 Controller 收到的请求已经是合法的 OAuth 请求，不需要重复校验协议参数。

### Grant Handler 插件模式

Token 端点使用插件化的 `IGrantHandler` 接口：

- 每个 grant type 对应一个 handler，通过 DI `AddKeyedSingleton` 注册
- Handler **无构造函数注入**，依赖从 `context.RequestServices` 运行时解析
- 新增 grant type：实现 `IGrantHandler` → `AddKeyedSingleton<IGrantHandler, MyHandler>(MyHandler.GrantType)`

当前已实现的 handler：

| Grant Type | Handler | 说明 |
|------------|---------|------|
| `password` | `PasswordGrantHandler` | 用户名+密码直接换 token |
| `phone_code` | `PhoneCodeGrantHandler` | 手机号+验证码换 token |
| `authorization_code` | `AuthorizationCodeGrantHandler` | 标准授权码 + refresh_token 轮转 |

### Access Token 未加密

`Program.cs` 中调用 `DisableAccessTokenEncryption()`，access token 以标准 JWT 格式输出，第三方 API 可以用原生 `Microsoft.AspNetCore.Authentication.JwtBearer` 验证。如果用作内部 STS 且不需要第三方验证，注释掉这行可以启用 OpenIddict 的加密格式，提高安全性。

### 安全注意事项

- **CORS**: 当前配置 `SetIsOriginAllowed(_ => true).AllowCredentials()`，生产环境需改为白名单
- **JWT 验证**: API 项目的 `ValidateAudience`/`ValidateIssuer` 应设为 `true`
- **凭据管理**: 数据库连接串、API Key 等敏感配置应从环境变量或密钥管理服务获取
- **限速**: 登录、token、短信端点建议添加 rate limiting 中间件
