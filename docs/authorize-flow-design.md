# /connect/authorize — 完整设计 & 代码逻辑流程

## 1. 入口

```
GET/POST /connect/authorize?response_type=code&client_id=spa-client&scope=openid+profile+api1&redirect_uri=http://localhost:5175/callback&state=xxx
```

由 OpenIddict 内置中间件接收，解析为 `OpenIddictRequest`。启用 **passthrough** 模式，所以
请求会继续走到 `AuthorizationController.Authorize()`。

```csharp
// Program.cs
options.SetAuthorizationEndpointUris("/connect/authorize")
       .UseAspNetCore()
       .EnableAuthorizationEndpointPassthrough()  // ← 请求继续往下走
```

---

## 2. 完整校验流程

```
Authorize()
│
├─ ①　验证 OpenIddict Request
│   └── 空 → 400 {"error":"invalid_request"}
│
├─ ②　验证 Client 是否存在
│   ├── 否 → Forbid (invalid_client)
│   └── 是 → 取 application 对象
│
├─ ③　验证用户认证状态
│   ├── AuthenticateAsync() (默认方案, Identity.Application cookie)
│   │
│   ├── 未登录 + prompt=none
│   │   └── Forbid (login_required)
│   │
│   ├── 未登录 + prompt=login 或其它
│   │   └── Challenge → 302 → /account/login?returnUrl=...
│   │
│   └── 已登录 → 取 user
│       └── user 不存在 → Forbid (invalid_grant)
│
├─ ④　验证 max_age (会话过期)
│   ├── max_age=60 + 已认证 > 60s
│   │   ├── prompt=none → Forbid (login_required)
│   │   └── 否则 → Challenge → 重登录
│   └── 通过 → 继续
│
├─ ⑤　验证 prompt=login (强制登录)
│   └── SignOutAsync → Challenge → 重登录
│
├─ ⑥　判断 Consent 类型
│   ├── 从 Application.ConsentType 读取
│   │   ├── "explicit" → 查永久授权记录
│   │   └── 其它 (implicit/silent) → 免同意
│   │
│   ├── 无已有授权 + explicit
│   │   └── Redirect → /consent?returnUrl=...
│   │       └── consent 页 → POST /api/consent → CreateAsync(授权)
│   │           └── 302 → /connect/authorize... (回到这里)
│   │
│   └── 已有授权 / 免同意
│       └── 继续
│
├─ ⑦　签发 ClaimsPrincipal
│   ├── CreateUserPrincipalAsync(user)
│   ├── SetScopes(request.GetScopes())
│   ├── SetResources(scopeManager.ListResourcesAsync)
│   ├── SetDestinations(GetDestinations)
│   └── SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
│       └── OpenIddict 生成 authorization_code, 302 到 client 回调
│
└─ 结束
```

---

## 3. 错误响应汇总

| 错误 | HTTP | error | error_description |
|------|------|-------|-----------------|
| 无 OpenIddict request | 400 | `invalid_request` | ... |
| Client 不存在 | 403 | `invalid_client` | Application not found |
| 未登录 + prompt=none | 403 | `login_required` | Authentication is required |
| 未登录 | 302 | — | Challenge → login |
| User 不存在 | 403 | `invalid_grant` | User not found |
| 会话过期 + prompt=none | 403 | `login_required` | User session expired |
| 会话过期 | 302 | — | Challenge → login |
| prompt=login | 302 | — | SignOut + Challenge → login |
| Consent 拒绝 | 通过 `/api/consent` | — | Forbid → 302 回 client 报拒绝 |

---

## 4. Consent 流程详情

### 4.1 GET /api/consent?returnUrl=...

```
从 returnUrl 中解析 client_id + scope
→ 查 applicationManager.GetDisplayNameAsync(application)
→ 分离 identity scopes (openid/profile/email 等) 和 resource scopes (api1 等)
→ 返回 {
    data: {
      clientName,
      identityScopes: [{name,displayName,required,checked}],
      resourceScopes: [{...}],
      returnUrl
    }
  }
```

### 4.2 POST /api/consent

```
接收: { button: "yes"|"no", scopesConsented: [], returnUrl: "" }

→ no → Forbid (access_denied) → 302 回 client

→ yes:
    ├── 从 returnUrl 提取 client_id
    ├── CreateUserPrincipalAsync(user)
    ├── SetScopes(scopesConsented)
    ├── authorizationManager.CreateAsync(永久授权)
    └── 返回 { code: 200, location: returnUrl }
        └── 前端 window.location = returnUrl → 重新进入 Authorize()
```

---

## 5. Token 签发 (POST /connect/token)

```
Authorize 成功后签发 authorization_code
→ 客户端用 code + PKCE 换 token:
    POST /connect/token?grant_type=authorization_code&code=xxx&code_verifier=xxx

AuthorizationController.Exchange()
├── 根据 grant_type 匹配 GrantHandler
│   ├── "password" → PasswordGrantHandler
│   ├── "phone_code" → PhoneCodeGrantHandler
│   └── "authorization_code" / "refresh_token" → AuthorizationCodeGrantHandler
│       └── AuthenticateAsync(OpenIddictServer) 验证 code
│           └── 成功 → SignIn → 返回 access_token + id_token + refresh_token
└── 未匹配 → 400 unsupported_grant_type
```

---

## 6. 数据流图

```
spa-client                          OpenIddictUI                            OpenIddictUI.Api
   │                                    │                                       │
   │  1. /connect/authorize             │                                       │
   │ ───────────────────────────────────>                                       │
   │                                    │                                       │
   │  2. Challenge → /account/login     │                                       │
   │ <───────────────────────────────────                                       │
   │                                    │                                       │
   │  3. POST /account/login            │                                       │
   │ ───────────────────────────────────>                                       │
   │  4. Set-Cookie: idsrv              │                                       │
   │ <───────────────────────────────────                                       │
   │                                    │                                       │
   │  5. 302 → /connect/authorize...    │                                       │
   │ <───────────────────────────────────                                       │
   │                                    │                                       │
   │  6. Authorize() → consent 检查     │                                       │
   │  7. GET /api/consent (显示 UI)     │                                       │
   │ <───────────────────────────────────                                       │
   │  8. POST /api/consent (用户批准)    │                                       │
   │ ───────────────────────────────────>                                       │
   │  9. 302 → returnUrl (authorize)    │                                       │
   │ <───────────────────────────────────                                       │
   │                                    │                                       │
   │ 10. Authorize() → 授权通过          │                                       │
   │ 11. 302 → spa-client?code=xxx      │                                       │
   │ <───────────────────────────────────                                       │
   │                                    │                                       │
   │ 12. POST /connect/token            │                                       │
   │ ───────────────────────────────────>                                       │
   │ 13. access_token + id_token       │                                       │
   │ <───────────────────────────────────                                       │
   │                                    │                                       │
   │ 14. GET /api/me (Bearer token)     │                                       │
   │ ──────────────────────────────────────────────────────────────────────────>
```

---

## 7. 关键设计决策

| 决策 | 理由 |
|------|------|
| passthrough 模式 | 不启用 OpenIddict 内置的 authorize 处理，让 ASP.NET Core 完全控制 |
| 硬编码管道 → GrantHandler | 插件化 grant 处理，`IGrantHandler` 可扩展 |
| GrantHandler stateless | 无构造函数注入，依赖从 `RequestServices` 运行时解析，可注册为任意生命周期 |
| consent 参数从 returnUrl 提取 | 前端不需要传 client_id，只需传 returnUrl + scopesConsented |
| ApiResult 封装 | 统一响应格式 `{ code, success, message, data }` |
| Serilog 日志 | 每个关键节点记录结构化日志 |
| `HttpContext.AuthenticateAsync()` 无参 | 使用 `DefaultAuthenticateScheme`，支持配置多方案 |
