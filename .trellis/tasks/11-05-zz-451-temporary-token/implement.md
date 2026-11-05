# ZZ-450：实施与验证计划（修订版）

## 门禁与交付单元

- 本修订规划待用户另行确认；确认前不启动新一轮实现、不修改业务代码、不提交或推送。此前任务缓存即使显示 `in_progress` 也不代表本修订规划已获批准。
- 作为一个端到端迭代交付：签发、示例只读接入、测试和文档共同就绪，不先部署只有标记而无只读强制的半成品。保持平台工作分支，在确认合法交付分支及 PR 目标后从 HEAD 推送，不切换分支、不强推、不自动合并；未经单独授权不提交 Trellis 部署产物或运行时文件。

## 有序检查清单

1. 从 issue 恢复经确认的最新 PRD 和规划，核对 worktree、已有变更、交付分支和 PR 目标；按需重建 Trellis 缓存并运行预开发检查，保留无关变更。
2. 以隔离测试验证来源 Bearer 的类型、签名、issuer、过期和 JWT payload 保真；验证复用服务端签名凭据后 `typ`、`kid`、discovery/JWKS、OpenIddict 本地验证与示例 API 互通。明确协议内部字段的安全处理和不可兼容情况，不能证明时停止报告。
3. 实现 Bearer-only 签发入口、输入验证、默认关闭的 scope/audience 配置和声明复制/冲突策略；限制期限、添加只读标记、禁止链式签发、不返回 refresh token，正确设置无缓存响应及 401/400/403。
4. 改造示例 API 的 issuer/audience/scope/时效校验和按明确业务操作分类的只读策略；覆盖只读、写入、未分类与普通令牌的既有授权。
5. 添加安全负面和端到端测试；编写配置、请求响应、示例调用、声明兼容条件、接收方接入清单及上线/回滚文档，检查日志和错误不泄露令牌。
6. 跨层自查后运行构建、格式、针对性及可运行的全量测试；逐项说明外部依赖及基线失败。安全门禁通过后提交、推送并创建或更新 PR，不自动合并；评论报告结果并等待用户确认。

## 测试矩阵

- **认证**：合法用户、合法服务身份、无需用户来源标记的旧令牌、仅 Cookie、无 Bearer、伪造签名、其他 issuer、ID token、过期/未生效、缺少 scope/audience、临时令牌链式申请。必须针对同一个已验证 JWT 读取 payload。
- **声明**：角色及自定义字符串、数组、对象、数字、布尔值的值和结构保持；新旧 JWT 元数据正确区分；重复字段、保留标记冲突、协议内部状态不兼容、无法安全保留的业务声明均拒绝；不得从数据库或请求注入权限。对于 scope 缩小及其与其他授权声明耦合的情况验证不增权，无法证明时拒绝。
- **授权与边界**：默认完整 scope、合法子集、超集、空数组、重复/非法值、大小写差异、缺省及非法配置、未允许 scope/audience、来源多个 audience 和映射不一致；逐项核对输出 scope/audience 不扩大，默认不静默缩减。
- **时效与保密**：验证 `exp <= min(now+300s, source.exp)`、即将到期、恰好到期和过期边界，零时钟容差；响应不缓存、无 refresh token；日志和错误不泄露来源或结果令牌。
- **示例 API**：通过真实签名及认证处理器验证临时 token 多次读取成功、写入和未分类操作拒绝；错误 issuer、audience、scope、签名及过期拒绝；普通有效 token 的既有授权行为保持。避免仅 mock 授权逻辑。
- **回归**：验证常规 OAuth code/password/phone、已有 claims 与普通示例访问；文档明确收紧 issuer/audience 的兼容影响。使用隔离密钥和配置，不调用真实外部账户。

## 验证命令（实施时执行并记录）

```bash
dotnet build OpenIddictUI.slnx
dotnet format OpenIddictUI.slnx --verify-no-changes --no-restore
dotnet test tests/OpenIddictUI.Tests/OpenIddictUI.Tests.csproj --filter "FullyQualifiedName~TemporaryToken"
dotnet test OpenIddictUI.slnx
```

如新增示例 API 测试项目，补充其命令并确保纳入解决方案。现有 `OidcFlowTests` 依赖外部服务和账号，全量测试可能无法独立运行；须单列实际结果与可复现条件。格式问题须区分既有基线和本次变更。

## 风险控制与回滚点

- 高风险区域：两个 `Program.cs`、签名凭据、JWT payload 原样保留与元数据重建、scope/audience 策略、示例授权策略。安全断言失败、普通合法 token 回归或无法证明声明语义时，不继续交付。
- 上线前确认共享 audience 的所有服务已实施只读及严格校验；默认不启用分享。停止签发可清空白名单，保留接收端防护直到现存临时令牌过期，不能立即撤销已分发 JWT。
- 完成后提交审核并等待用户验收；不自动标记 done。

---

Trellis 初始化时补写了开发者身份；生成的 joiner/bootstrap 引导任务不执行。规划文档为本轮缓存，完整内容已列于本评论，未授权提交 Trellis 产物。

**请确认是否按这份修订后的规划开始实施？**
