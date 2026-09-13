# Application 管理契约

本文记录当前 Application 管理接口和 STS 管理前端编辑窗口之间的实际契约。接口位于 `api/applications`，实现见 `src/OpenIddictUI/Controllers/ApplicationsController.cs`；本文不包含任何真实 `clientSecret` 或 JWKS 内容。

## 结论

- 列表和详情接口只返回可用于展示和编辑的非敏感字段；`clientSecret` 和 `jsonWebKeySet` 不会出现在响应中。
- 所有 Application 写操作由管理员用户执行。编辑时凭据输入框保持空白；省略、`null`、空字符串或仅空白的凭据表示“保留旧值”，不能用空值清除 confidential 凭据。
- `clientType` 只接受 `public` 和 `confidential`，比较时忽略首尾空白和大小写，写入和返回统一为小写；存量 `null` 按 `public` 处理。
- URI 只接受带 host 的绝对 `http`/`https` URI；`authorization_code` 必须同时提供至少一个有效 `redirectUris`。
- 六类 lifetime 以正整数秒传输。创建时可省略；更新时省略或传 `null` 保留已有设置，传正数才覆盖对应设置。

## 权限与统一响应

`ApplicationsController` 使用 `[Authorize]`，并且列表、详情、创建、更新都会额外要求 `User.Identity.Name == "admin"`。非管理员请求返回 HTTP 401，响应体仍为统一的 `ApiResult`，典型结构如下：

```json
{
  "code": 401,
  "success": false,
  "message": "Not authenticated"
}
```

接口在控制器内发现业务校验、未知 id 或请求体错误时，通常返回 HTTP 200，并通过 `ApiResult` 表示失败：

```json
{
  "code": 400,
  "success": false,
  "message": "请求参数不合法"
}
```

`code == 200` 且 `success == true` 表示成功；成功详情和列表数据位于 `data`。JSON 反序列化失败、空请求体和字段校验失败也按 `code == 400` 的结构化错误处理，且不会创建或更新 Application。

## 接口

| 方法 | 路径 | 行为 |
|---|---|---|
| `GET` | `/api/applications` | 获取列表；不返回 lifetime 和敏感凭据 |
| `GET` | `/api/applications/{id}` | 按 OpenIddict Application id 获取完整可编辑详情 |
| `POST` | `/api/applications` | 创建 Application |
| `PUT` | `/api/applications/{id}` | 更新 Application；`id` 不存在时不写入 |

### 列表响应

`data` 是数组，每一项包含以下字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | string | OpenIddict Application id |
| `clientId` | string | 客户端标识 |
| `displayName` | string/null | 展示名称 |
| `clientType` | string | 规范化为 `public` 或 `confidential` |
| `applicationType` | string | 存量为空时为 `web` |
| `consentType` | string | 存量为空时为 `implicit` |
| `redirectUris` | string[] | 授权回调 URI |
| `postLogoutRedirectUris` | string[] | 登出后回调 URI |
| `grantTypes` | string[] | 从 `gt:` permission 提取的 grant type |
| `scopes` | string[] | 从 `scp:` permission 提取的 scope |
| `clientUrl` | string/null | `client_url` setting |
| `clientLogoUrl` | string/null | `client_logo_url` setting |
| `enabled` | string | setting 的文本值，缺省为 `"true"`，不是 JSON boolean |

列表不包含 `requirePkce` 和六类 lifetime；列表和详情都不包含 `clientSecret`、`jsonWebKeySet`。存量 `clientType == null` 的列表项返回 `clientType: "public"`；无法规范化的存量类型返回 `code == 400` 的结构化错误。

### 详情响应

详情包含上表所有字段，并额外包含：

| 字段 | 类型 | 说明 |
|---|---|---|
| `requirePkce` | boolean | 是否要求 PKCE；`public` 客户端创建/更新时始终要求 |
| `accessTokenLifetime` | integer/null | access token 有效期，单位秒 |
| `authorizationCodeLifetime` | integer/null | authorization code 有效期，单位秒 |
| `refreshTokenLifetime` | integer/null | refresh token 有效期，单位秒 |
| `identityTokenLifetime` | integer/null | identity token 有效期，单位秒 |
| `deviceCodeLifetime` | integer/null | device code 有效期，单位秒 |
| `userCodeLifetime` | integer/null | user code 有效期，单位秒 |

缺失或无法解析的 lifetime setting 映射为 `null`，不会使详情请求失败。成功响应示例只展示非敏感字段：

```json
{
  "code": 200,
  "success": true,
  "data": {
    "id": "app-1",
    "clientId": "demo-public",
    "displayName": "Demo client",
    "clientType": "public",
    "applicationType": "web",
    "consentType": "implicit",
    "redirectUris": ["https://client.example/callback"],
    "postLogoutRedirectUris": ["https://client.example/logout"],
    "grantTypes": ["authorization_code"],
    "scopes": ["openid"],
    "clientUrl": null,
    "clientLogoUrl": null,
    "enabled": "true",
    "requirePkce": true,
    "accessTokenLifetime": null,
    "authorizationCodeLifetime": null,
    "refreshTokenLifetime": null,
    "identityTokenLifetime": null,
    "deviceCodeLifetime": null,
    "userCodeLifetime": null
  }
}
```

未知 id 的详情请求返回 HTTP 200，`code == 4012`、`success == false`、`message == "用户不存在"`。更新接口对未知 id 也使用相同结果。

## 创建与更新请求

`POST` 和 `PUT` 的 JSON body 使用同一组 `ApplicationInput` 字段。`clientId` 两种操作都必须提供，最大长度 100；更新时路径中的 `{id}` 决定被更新的对象，body 中的 `clientId` 仍会参与模型校验和写入。

| 字段 | 类型 | 创建/更新规则 |
|---|---|---|
| `clientId` | string | 必填，最长 100；创建时不能与已有 client id 重复 |
| `clientSecret` | string/null | 可选，最长 512；具体语义见“敏感凭据” |
| `jsonWebKeySet` | string/null | 可选；非空白值必须能被 `JsonWebKeySet` 解析 |
| `displayName` | string/null | 可选，最长 200 |
| `clientType` | string/null | `public` 或 `confidential`；`null` 按 `public` |
| `consentType` | string/null | 省略时写入 `implicit` |
| `applicationType` | string/null | 省略时写入 `web` |
| `redirectUris` | string[]/null | 每项必须是合法的绝对 HTTP/HTTPS URI |
| `postLogoutRedirectUris` | string[]/null | 每项必须是合法的绝对 HTTP/HTTPS URI |
| `grantTypes` | string[]/null | 直接重建 `gt:` permissions |
| `scopes` | string[]/null | 直接重建 `scp:` permissions |
| `clientUrl` | string/null | 可选，最长 512 |
| `clientLogoUrl` | string/null | 可选，最长 512 |
| 六类 `*Lifetime` | integer/null | 可选；正整数，单位秒 |
| `requirePkce` | boolean | 可选；`public` 客户端始终为 true |
| `enabled` | boolean | 可选，服务端默认 true |

一个不含敏感值的 public client 创建示例：

```bash
curl -b "$COOKIE_JAR" -H 'Content-Type: application/json' \
  -X POST "$STS_BASE/api/applications" \
  --data '{
    "clientId": "demo-public",
    "displayName": "Demo client",
    "clientType": "public",
    "applicationType": "web",
    "consentType": "implicit",
    "redirectUris": ["https://client.example/callback"],
    "postLogoutRedirectUris": ["https://client.example/logout"],
    "grantTypes": ["authorization_code"],
    "scopes": ["openid"],
    "requirePkce": true,
    "enabled": true
  }'
```

`$STS_BASE` 和 `$COOKIE_JAR` 只是调用方环境变量；confidential client 的 secret 或 JWKS 应从 secret manager 等安全来源注入，本文不提供可用值。

## 敏感凭据

### 创建

- `confidential` 客户端必须至少提供一个非空白的 `clientSecret` 或 `jsonWebKeySet`。两者都为省略、`null`、空字符串或仅空白时返回 `code == 400`，消息为 `confidential 客户端必须设置 ClientSecret 或 JWKS`。
- 非空白 `jsonWebKeySet` 会先解析；解析失败返回 `code == 400`，消息为 `JsonWebKeySet 格式不合法`，不会写入。
- `public` 客户端不能提供非空白 `clientSecret`，否则返回 `code == 400`，消息为 `public 客户端不能设置 ClientSecret`。public 创建最终将 `ClientSecret` 写为 `null`。
- 合法的非空白 `clientSecret` 和 `jsonWebKeySet` 会分别写入 descriptor；空白凭据不会作为新值写入。

### 编辑

详情响应和前端 `applicationFormFromDetail()` 都不读取或回填敏感字段，即使服务端存有凭据，编辑窗口中的 `clientSecret` 和 `jsonWebKeySet` 也保持空白。编辑 payload 只有在用户输入非空白值时才发送对应字段。

| PUT 中的值 | `clientSecret` | `jsonWebKeySet` |
|---|---|---|
| 省略、`null`、空字符串或仅空白 | 保留旧值 | 保留旧值 |
| 合法非空白值 | 只覆盖 secret | 只覆盖 JWKS |

因此，更新一项凭据不会清除另一项；同时提供两个合法值时分别覆盖两项。空值不能清除 confidential 凭据。例外是切换到 `public`：服务端始终清除旧 `ClientSecret`；未提供新 JWKS 时，已有 `JsonWebKeySet` 仍按“保留旧值”处理。

## ClientType 规范化与切换

`clientType` 比较时会去除首尾空白并忽略大小写：`null`、`public`、`PUBLIC` 和带首尾空白的 public 值都会规范化为 `public`；`confidential` 同理。空字符串、仅空白和其他值均非法，返回 `code == 400`，消息为 `ClientType 必须是 public 或 confidential`。详情、列表和写入 descriptor 的值均使用小写结果。

编辑时，存量 `null` client type 先按 `public` 判断类型切换：

- `public -> confidential` 必须在本次请求中提供新的非空白 `clientSecret` 或 JWKS，不能只依赖存量凭据。
- `confidential -> public` 不能提供非空白 `clientSecret`；旧 secret 会被清除，旧 JWKS 在未提供新值时保留。
- 同类型编辑时，省略/空白凭据保留旧值，合法新值只覆盖对应凭据。

## URI、Grant 与 PKCE

`redirectUris` 和 `postLogoutRedirectUris` 的每一项都必须满足：

1. 非空白。
2. `UriKind.Absolute` 可解析。
3. scheme 为 `http` 或 `https`。
4. 存在非空 host。

因此，`//example.com/callback`、相对 URI、`file:`、`javascript:`、`data:`，以及缺少 host 的 `http:///callback`/`https:///callback` 都会被拒绝，返回 `code == 400` 的结构化错误。URI 合法性校验在创建和更新写入前执行；失败时不会调用 Application manager 的创建、填充或更新操作。

当 `grantTypes` 包含 `authorization_code` 时，`redirectUris` 必须至少有一项，并且每项都必须通过上述校验。写入 descriptor 时，服务端会同步维护由 grant、scope 和 URI 推导出的 permissions：authorization code 会增加 `response_type=code`，存在 redirect URI 会增加 authorization/token endpoint permission，存在 post-logout URI 会增加 end-session endpoint permission；更新提交的空列表会清除对应 URI 及其派生 permission。

public client 强制要求 PKCE；confidential client 按 `requirePkce` 写入。详情通过 `requirePkce` 返回当前要求。

## Token Lifetime

六个字段都使用“秒”作为 API 单位：

| 字段 | descriptor setting |
|---|---|
| `accessTokenLifetime` | access token |
| `authorizationCodeLifetime` | authorization code |
| `refreshTokenLifetime` | refresh token |
| `identityTokenLifetime` | identity token |
| `deviceCodeLifetime` | device code |
| `userCodeLifetime` | user code |

规则如下：

- 创建和更新时，非 `null` 值必须大于 0；0 或负数返回 `code == 400`，消息为对应字段名加 `必须大于 0`。
- JSON 中传入无法转换为整数的 lifetime 会进入模型校验错误，返回 HTTP 200、`code == 400`、`success == false`，且不执行写入。
- 创建时省略或传 `null` 表示本次不设置该项。
- 更新时每个字段独立处理：省略或 `null` 保留已有 setting，传入正整数才覆盖该项；其他五项不会因单项覆盖而被重置。
- 详情读取 setting 的 `TimeSpan` 并转换为整数秒；setting 缺失、格式非法或无法解析时，该字段返回 `null`。

## 编辑窗口加载行为

`frontend/src/views/ApplicationPage.vue` 点击编辑后使用列表项的 `id` 请求 `GET /api/applications/{id}`，不使用可能不完整的列表行填充表单。请求期间先清空旧编辑状态；只有响应 `code == 200` 且包含 `data` 时才打开详情表单。

- 详情请求失败、响应没有数据或请求抛异常时，旧表单不会复用，modal 保持关闭，并显示 `loadFailed` 提示。
- 编辑控制器为每次编辑、添加和关闭操作递增请求版本。只有最新版本的响应可以写入状态；较早的响应返回 `stale`，不会重新打开 modal 或覆盖当前选择。
- 用户在详情请求未完成时点击添加或关闭，待处理响应会失效；它不能把编辑窗口重新打开。

对应实现和测试：`src/OpenIddictUI/Controllers/ApplicationsController.cs:69`、`frontend/src/views/applicationForm.ts:81`、`frontend/src/views/applicationForm.ts:132`、`tests/OpenIddictUI.Tests/Controllers/ApplicationsControllerTests.cs:16`、`frontend/tests/applicationForm.test.mjs:204`。
