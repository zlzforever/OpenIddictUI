# ZZ-450 只读临时访问令牌：技术设计（修订版）

## 1. 边界与现状

- 仅允许本系统有效 Bearer access token，不区分人和服务；不再以缺少用户来源标记拒绝旧令牌。保留既定的只读、scope、audience 和有效期限制；无 Cookie 入口、页面或实时撤销。
- `src/OpenIddictUI/Program.cs` 已配置 OpenIddict 本地验证、未加密 JWT 和签名凭据，默认身份方案为 Identity Cookie。普通 API 不能依赖默认 `User` 验证 Bearer，也不能直接假定 OpenIddict 协议透传端点的 `SignIn` 可在普通 MVC 路由使用。
- 来源 grant 按 scope 解析资源；`Data/SeedData.cs` 创建 scope 时加入同名资源，但已有数据库的配置不一定相同。示例 API 目前关闭 issuer/audience 校验，仅有读取端点。

## 2. 请求与响应契约

- `POST /api/temporary-tokens`；仅从 `Authorization: Bearer <access_token>` 获取来源令牌，显式使用本地 OpenIddict validation 认证方案，不回退 Cookie。JSON 请求 `{}` 继承全部来源 scope；`{"scopes":["api1"]}` 申请非空子集。不得通过 query、body 或日志传递令牌，不允许请求方指定身份、角色或 audience；多余授权字段不得影响签发。
- 成功 HTTP 200，沿用 `ApiResult`，`data` 包含 `accessToken`、`tokenType`（`Bearer`）、`expiresAt`（UTC ISO 8601）和实际 `scopes`，不返回 refresh token。响应包含 `Cache-Control: no-store` 和 `Pragma: no-cache`，token 只出现在响应体。
- 缺失/无效 Bearer 为 401；格式错误、空数组、非法或重复 scope、scope 超集为 400；来源不可分享、目标未配置或策略不允许为 403。错误信息不回显凭据或敏感声明。

## 3. 认证、声明和签发

1. 使用可信本地验证方案验证来源签名、issuer、access-token 类型、`nbf` 和严格有效期，显式排除 ID token、其他 issuer、临时 token 和无效 token。兼容满足上述条件及目标策略的用户、服务和旧 access token；不引入新的用户来源检查。禁止仅解析 JWT 后就信任其内容。
2. **保留原声明**：从与已认证请求严格绑定的同一个原始 JWT 中提取已验证的 payload，保留身份、角色及自定义业务声明的原值、JSON 类型和结构（包括数组、对象、数字和布尔值）；不能经 `ClaimsPrincipal` 的字符串化、映射或去重造成改变。不从数据库或请求参数补充角色或权限。检测重复 JSON 属性、无法无损处理的结构，以及与保留标记冲突等情形，拒绝签发而非猜测或静默丢失。不得信任未经验证的 header/payload；验证与读取必须针对同一个令牌。
3. **必须重建的令牌元数据**：重新设置签发方、签发时间 `iat`、生效时间 `nbf`、到期时间 `exp`、新 `jti`，按规则设置 `scope`、`aud`，并设置受签名保护的 `token_use=temporary_readonly`。来源如已含该保留字段，无论值是什么均拒绝，避免覆盖业务语义或链式签发。JWT header 使用新签名对应的类型、算法和 `kid`；不得照搬来源签名及 header。对 OpenIddict 内部 token/authorization 标识等协议状态，先验证其语义并安全重建；若不能区分必要业务声明或无法安全处理则拒绝，不得静默丢弃业务信息或沿用不适用的旧状态。
4. **Scope 和受众**：scope 按大小写敏感集合比较；缺省表示完整继承，显式列表必须是非空来源子集。服务端配置 scope→允许 audience；每个所选 scope 必须在配置中，且至少一个相应 audience 同时存在于来源令牌。签发 audience 从这些匹配目标计算，始终是来源及允许目标的子集；任一 scope 不满足就拒绝，不静默缩减 scope。生产默认无允许目标，配置缺失或无效 fail closed。
5. 其他业务声明可能自身包含权限、目标或与 scope 耦合的结构。不能假设单独缩小 `scope` 就一定缩小所有业务权限：实施时必须验证实际可分享的声明语义和接收方授权；对无法证明安全保留、或与缩小后的权限产生冲突的令牌拒绝，并在文档说明兼容限制。不得悄悄改写或删除这类业务声明。
6. `exp = min(签发时刻 + 300 秒, 来源 exp)`，按 JWT 秒精度向下取整；剩余时间不足以形成有效 token 则拒绝。签发端和示例 API 设置零时钟容差并测试边界，不延长来源授权。新 token 不可刷新，也不能用于再次申请。
7. 优先复用 OpenIddict/IdentityModel 官方能力和本服务器现有签名凭据，不引入第二套签名密钥。先以隔离集成测试验证 OpenIddict 7.7.0 下本地验证、来源 JWT 内容无损读取、新 JWT 的 `typ`/`kid`/算法、discovery/JWKS 和接收方验证互通。直接构造 JWT 必须核实 OpenIddict 内部状态和校验语义，不能绕过有效性检查；无法同时满足安全、声明保留和兼容要求时停止并报告，不交付降级方案。

## 4. 示例 API 只读执行及兼容

- `src/OpenIddictUI.Api/Program.cs` 启用 issuer、签名、有效期、audience（示例 `api1`）及既有 scope 检查；生产使用 HTTPS metadata，本地 HTTP 只限 development。收紧 issuer/audience 是有意的兼容变化：过去被错误接受的无效或缺失 audience 的 token 将被拒绝，合法普通 token 仍遵循原有授权。
- 不按 HTTP 方法推断安全。为受保护操作明确声明业务分类；临时令牌只允许显式标记只读的操作，写入及未分类操作默认拒绝，普通 token 保持现有授权行为。使用不会更改真实数据的可观察测试写入端点或等价测试 host，覆盖未知分类。
- 每个仓库外接收方都必须验证签名、issuer、audience、scope、时间和标记，并按业务操作及既有数据权限执行；共享 audience 的所有服务均须接入。忽略标记的服务仍可能接受写入。

## 5. 文件范围、部署与回滚

- 预计新增 STS 临时令牌 controller、请求 DTO、配置选项和签发/策略服务；按需调整 `src/OpenIddictUI/Program.cs`，但不为区分真实用户修改既有 grant 或要求重新登录。
- 调整 `src/OpenIddictUI.Api/Program.cs`、示例 controller 或新增授权组件；增加单元与集成测试、运行说明。不修改前端、数据库 schema 或无关服务。
- 测试使用隔离签名密钥和配置，不使用生产凭据。上线前核实 issuer/audience 配置及所有共享目标服务的只读接入，再显式启用白名单。回滚时先清空白名单阻止新签发；已签发 JWT 仍有效，应保留接收端只读校验至最长有效期结束并考虑接收端时钟误差，无法立即撤销已分发的令牌。

## 6. 风险与技术门禁

- 保留复杂业务声明与缩小 scope 可能相互冲突；签发端不能只凭字段名推断所有自定义业务语义。若无法证明安全兼容，暂停并报告具体字段/影响，而不是声称已实现无损且不增权。
- 在普通 API 复用 OpenIddict 7.7.0 签名凭据、识别 access-token 类型及与 JWKS/本地验证互通仍需真实验证；不得因规划确认而视为已验证。
- 外部业务 API 若忽略标记、过度信任 audience 或使用宽松时钟容差，签发端不能单独保证只读和准时过期；上线配置及接入审查是必要前提。

---
