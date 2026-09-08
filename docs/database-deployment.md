# 跨数据库配置与部署

本文适用于当前跨数据库实现（`afd20a3`，包含 `e211668`）。运行时不改变既有 HTTP/API 路径；它只根据配置选择 PostgreSQL 或 MySQL 的 EF provider 与分布式缓存实现。

## 结论

- 支持 PostgreSQL，以及 MySQL 8.0 及以上版本。
- 不支持 MySQL 5.7、MariaDB 或其他未列出的 provider。
- 数据库选择由根配置 `Database` 决定；省略该键仍使用既有 PostgreSQL 路径。
- EF、OpenIddict 和分布式缓存始终读取 `ConnectionStrings:DefaultConnection`。不要另外配置 provider 专用的连接串。
- 没有 `AddDistributedMemoryCache` 之类的降级注册。数据库或分布式缓存不可用时，应用不会静默切换到内存缓存。
- 本功能不提供 PostgreSQL 与 MySQL 之间的数据搬迁工具。切换 provider 前必须自行规划数据复制、停机和回滚。

## Provider 选择

`Database` 的值会去除首尾空白并按不区分大小写匹配：

| 配置值 | 结果 |
|---|---|
| 未配置（键不存在） | PostgreSQL，兼容既有配置 |
| `postgres` | PostgreSQL |
| `postgre` | PostgreSQL，兼容别名 |
| `mysql` | MySQL |
| 空字符串、仅空白、`postgresql`、`sqlite` 或其他值 | `CreateWebApplication` 阶段失败 |

“未配置”和“配置为空”不是同一行为：只有键不存在时才使用 PostgreSQL 默认值。部署环境应显式设置 `Database`，避免误判。

## 连接与缓存配置

### 公共配置规则

`ConnectionStrings:DefaultConnection` 同时作为 EF 数据库和分布式缓存的连接入口。推荐通过 secret manager、容器 secret 或环境变量注入，不要把密码写入提交的 JSON、镜像层或日志。

仓库现有 `src/OpenIddictUI/appsettings.json` 使用 `${SOCODB_DB_USER}` 和 `${SOCODB_DB_PASSWORD}` 占位符，未设置 `Database`，因此保持 PostgreSQL 默认路径。运行时会替换已存在的环境变量；Docker 的 `docker-entrypoint.sh` 还支持通过 `CONFIG_SOURCE` 生成配置文件。通用 .NET 环境变量写法是用双下划线映射层级键。

以下示例只使用占位符，不包含可用凭据。PostgreSQL 与 MySQL 选择其一：

```bash
# PostgreSQL
export Database=postgres
export ConnectionStrings__DefaultConnection='Host=<pg-host>;Port=5432;Database=<db>;Username=<user>;Password=<password>;Pooling=true;'
```

```bash
# MySQL
export Database=mysql
export ConnectionStrings__DefaultConnection='Server=<mysql-host>;Port=3306;Database=<db>;User ID=<user>;Password=<password>;'
export MySqlCache__SchemaName='<schema-or-database>'
export MySqlCache__TableName='openiddict_cache_entries'
export MySqlCache__ExpiredItemsDeletionInterval='00:30:00'
export MySqlCache__DefaultSlidingExpiration='00:20:00'
```

### PostgreSQL

当 `Database` 解析为 PostgreSQL 时，应用注册 `Microsoft.Extensions.Caching.Postgres.PostgresCache`，并使用 `PostgresCache` 配置段：

| 配置键 | 当前样例值 | 代码默认值 | 说明 |
|---|---:|---:|---|
| `PostgresCache:SchemaName` | `public` | `public` | 缓存表所在 schema |
| `PostgresCache:TableName` | `openiddict_cache_entries` | `cache` | 当前仓库样例显式覆盖了代码默认值 |
| `PostgresCache:CreateIfNotExists` | `true` | `true` | 允许 provider 在需要时创建缓存表 |
| `PostgresCache:UseWAL` | `false` | `false` | PostgreSQL cache provider 选项 |
| `PostgresCache:ExpiredItemsDeletionInterval` | `00:30:00` | provider 默认值 | `TimeSpan` 文本；当前代码仅在能解析时覆盖 provider 默认值 |
| `PostgresCache:DefaultSlidingExpiration` | `00:20:00` | provider 默认值 | `TimeSpan` 文本；当前代码仅在能解析时覆盖 provider 默认值 |

`DatabaseStartup.InitializeAsync` 对 PostgreSQL 直接返回，不会在 EF migration 前读取缓存。随后 `SeedData.ApplyAsync` 先调用 `AppDbContext.Database.MigrateAsync()`；缓存表是否自动创建由 `CreateIfNotExists` 以及 PostgreSQL cache provider 的实际操作决定。将 `CreateIfNotExists` 设为 `false` 时，必须预先创建并授权该表，缓存首次使用时的 schema/连接问题会直接暴露。

### MySQL

当 `Database` 解析为 MySQL 时，EF 使用 `MySqlAppDbContext`，分布式缓存使用 `Pomelo.Extensions.Caching.MySql.MySqlCache`。MySQL 配置在应用创建阶段解析：

| 配置键 | 缺省值/约束 | 说明 |
|---|---|---|
| `MySqlCache:SchemaName` | 缺省为 `DefaultConnection` 中的数据库名 | MySQL 中 schema 等同 database；值必须是安全标识符 |
| `MySqlCache:TableName` | `openiddict_cache_entries` | 必须是安全标识符 |
| `MySqlCache:ExpiredItemsDeletionInterval` | 未配置时为 `null`；样例为 `00:30:00` | 配置后必须是大于零的 `TimeSpan` |
| `MySqlCache:DefaultSlidingExpiration` | `00:20:00` | 必须是大于零的 `TimeSpan` |

安全标识符需匹配 `[A-Za-z_][A-Za-z0-9_]{0,63}`。连接串中的 database 名也会作为默认 `SchemaName` 校验；包含连字符等字符时请显式设置合法的 `MySqlCache:SchemaName`。

MySQL 连接串会开启 `AllowUserVariables`，以兼容 cache provider 的 upsert 查询。不要把 `MySqlCache` 当作第二个连接入口；它只配置 schema、表名和过期策略。

## 启动顺序与失败行为

STS 的 `Program.Main` 按以下顺序执行：

```text
CreateWebApplication
  -> 解析 Database，注册对应 EF provider 与 IDistributedCache
  -> DatabaseStartup.InitializeAsync
  -> SeedData.ApplyAsync
       -> EF Database.MigrateAsync()
       -> 读取 openiddict-seed.json 并补齐不存在的 scope/client
  -> RunAsync
```

provider 的差异如下：

| 阶段 | PostgreSQL | MySQL |
|---|---|---|
| 应用创建 | 注册 Npgsql `AppDbContext` 与 PostgreSQL distributed cache | 注册 Pomelo `MySqlAppDbContext` 与 MySQL distributed cache；保留 `AppDbContext` 解析别名 |
| `DatabaseStartup` | 不连接、不 probe 缓存，等待后续 EF migration | 连接数据库，执行 `SELECT VERSION()`，拒绝低于 8.0 或 MariaDB，创建并校验缓存表，再执行一次 `IDistributedCache` probe |
| EF migration | `SeedData.ApplyAsync` 中执行 | 在缓存表准备和 probe 成功后执行 |
| 连接/缓存失败 | 不会内存降级；错误会在实际 provider 操作时失败 | 启动阶段失败，不进入 `RunAsync`；不通过缓存 probe 后不会继续启动 |

MySQL 缓存表必须在 probe 前可用；预先存在但结构不兼容的表也会使启动失败。生产环境需给应用账号授予对应 database 的连接、cache 表 DDL/查询与 EF migration 所需权限，并避免让多个 provider 部署共用同一份业务数据库。

## MySQL 缓存表

应用会执行等价于下列 DDL 的 `CREATE TABLE IF NOT EXISTS`，其中 schema 和表名来自配置：

```sql
CREATE TABLE IF NOT EXISTS `<schema>`.`<table>` (
    `Id` varchar(449) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    `Value` longblob NOT NULL,
    `ExpiresAtTime` datetime(6) NOT NULL,
    `SlidingExpirationInSeconds` bigint NULL,
    `AbsoluteExpiration` datetime(6) NULL,
    PRIMARY KEY (`Id`),
    KEY `ix_expires_at_time` (`ExpiresAtTime`)
) ENGINE=InnoDB;
```

启动时还会查询 `information_schema.COLUMNS` 和 `information_schema.STATISTICS`，校验以下契约：

- `Id` 为 `varchar(449) NOT NULL`，字符集/排序规则为 `ascii`/`ascii_bin`。
- `Value` 为 `longblob NOT NULL`。
- `ExpiresAtTime` 为 `datetime(6) NOT NULL`。
- `SlidingExpirationInSeconds` 为可空 `bigint`。
- `AbsoluteExpiration` 为可空 `datetime(6)`。
- `Id` 是主键，且 `ExpiresAtTime` 有以该列为首列的索引。

因此，手工建表或复用旧表时不能只检查表名；字段类型、可空性、精度、排序规则和索引都必须满足以上契约。

## EF Migration 与历史隔离

迁移文件和 design-time context 已按 provider 分开：

| provider | context | 目录 | migration ID |
|---|---|---|---|
| PostgreSQL | `AppDbContext` | `src/OpenIddictUI/Migrations/Postgre` | `20260527135000_OpenIddictSchema` |
| MySQL | `MySqlAppDbContext` | `src/OpenIddictUI/Migrations/MySql` | `20260907121156_MySqlOpenIddictSchema` |

两个 context 使用不同的 migrations assembly 和迁移列表。历史表默认名都是 `openiddict_migrations_history`，由 `OpenIddict:MigrationsHistoryTable` 覆盖；历史记录随 `DefaultConnection` 指向的数据库物理隔离。默认表名本身不是 provider 标识，因此不要把两个 provider 指向同一业务数据库/历史表；如部署策略确实需要共享物理库，必须为每个 provider 配置不同的历史表名并先验证权限与迁移状态。

Identity 表使用 `IdentityExtension:Tables` 配置的表名，并在 EF model 中标记为 `ExcludeFromMigrations`；上述 migration 不会创建或升级 Identity 表。部署前必须由外部用户系统或数据库脚本准备这些表及其权限。

设计时迁移命令必须显式指定 context，并在 `src/OpenIddictUI` 目录执行，使 `appsettings*.json` 能被 design-time factory 读取：

```bash
cd src/OpenIddictUI

# PostgreSQL：只应用 Migrations/Postgre 中的迁移
ConnectionStrings__DefaultConnection='Host=<pg-host>;Port=5432;Database=<db>;Username=<user>;Password=<password>;' dotnet ef database update --context AppDbContext

# MySQL：只应用 Migrations/MySql 中的迁移
ConnectionStrings__DefaultConnection='Server=<mysql-host>;Port=3306;Database=<db>;User ID=<user>;Password=<password>;' dotnet ef database update --context MySqlAppDbContext
```

这些命令只负责目标 provider 已存在的 EF migration，不会复制 Identity、OpenIddict、cache 或其他业务数据，也不会把 PostgreSQL schema 转换成 MySQL schema。迁移生成也必须绑定正确的 context，禁止把一个 provider 的 migration 目录用于另一个 provider。

## 集成验证

### 所需服务与凭据

| 验证目标 | 必需服务 | 必需凭据/数据 |
|---|---|---|
| PostgreSQL | 可访问的 PostgreSQL 实例 | `DefaultConnection` 对应的数据库账号；账号需有 EF migration 和 PostgreSQL cache 表所需权限 |
| MySQL | 可访问的 MySQL 8.0+ 实例；不得使用 5.7/MariaDB | `DefaultConnection` 对应的数据库账号；账号需有版本查询、cache 表 DDL/校验及 EF migration 权限 |
| OIDC 完整流程 | 可访问的 STS；若验证 API 还需 API 服务；反向代理路径需与测试一致 | 已迁移的 OpenIddict/Identity 数据、测试用户、测试客户端及匹配 redirect URI；凭据只从隔离测试 secret 注入 |

仓库当前 `openiddict-seed.json` 定义的是 public client `spa-client`，redirect URI 为 `http://localhost:5175/...`。`OidcFlowTests` 则固定查找 `sample-app`，并使用源码中的隔离测试账号常量，因此不能把运行 seed 后的 `spa-client` 自动视为该测试的完整 fixture。测试账号密码不应复制到部署配置或生产环境。

### 可执行命令

先用目标 provider 的环境变量启动 STS，再执行健康检查和 discovery：

```bash
Database=postgres ConnectionStrings__DefaultConnection='Host=<pg-host>;Port=5432;Database=<db>;Username=<user>;Password=<password>;' dotnet run --project src/OpenIddictUI --urls http://localhost:5164

curl -fsS http://localhost:5164/healthz
curl -fsS http://localhost:5164/.well-known/openid-configuration
```

MySQL 验证只需将 `Database` 和连接串替换为 MySQL 版本，并确保 `MySqlCache` 配置与实际表权限一致。数据库 provider 的定向测试命令为：

```bash
dotnet test tests/OpenIddictUI.Tests/OpenIddictUI.Tests.csproj --filter 'FullyQualifiedName~DatabaseProviderTests'
```

OIDC 直连测试命令为：

```bash
OPENIDDICT_TEST_URL=http://localhost:5164 dotnet test tests/OpenIddictUI.Tests/OpenIddictUI.Tests.csproj --filter 'FullyQualifiedName~OidcFlowTests'
```

该测试会请求 `${OPENIDDICT_TEST_URL}/openid/...`，需要部署的反向代理提供对应路径映射；它还需要 `sample-app`、测试用户和 `${OPENIDDICT_TEST_URL}/wildgoose/signin-oidc` redirect fixture。测试覆盖登录、授权码、PKCE、token 和 UserInfo，不覆盖真实分布式 cache 的全部生命周期。

### 当前验证边界

| 项目 | 状态 |
|---|---|
| provider 解析、别名、空/非法值 | 已由 `DatabaseProviderTests` 覆盖 |
| MySQL 8.0 下限、MariaDB/5.7 拒绝 | 已由定向测试覆盖 |
| MySQL cache 建表、字段/索引校验、启动顺序 | 已由定向测试覆盖 |
| 缓存仅使用数据库实现、无内存降级注册 | 已由定向测试覆盖 |
| 真实 PostgreSQL 连接与 migration | 当前环境未执行 |
| 真实 MySQL 连接、cache set/get/remove | 当前环境未执行 |
| TTL、滑动过期、过期清理 | 当前环境未执行；仓库没有覆盖这些行为的独立 smoke 命令 |
| 完整 OIDC 外部流程 | 当前环境未执行；需要上述服务、数据、凭据和路径 fixture |

因此，定向测试通过不等同于两个数据库的真实集成已验收，也不等同于 cache 的 set/get/remove、TTL、滑动过期和清理行为已验证。

## 上线与回滚

上线前：

1. 为目标数据库、Identity 表、OpenIddict 表和必要的 cache 数据做备份，并确认应用账号权限。
2. 一起切换 `Database` 与 `ConnectionStrings:DefaultConnection`；不要只切换其中一个。
3. MySQL 先确认服务端 `SELECT VERSION()` 为 8.0+ 且不是 MariaDB；确认 cache 表名/schema 可用。
4. 首次发布建议只启动一个实例完成 EF migration、seed 和 MySQL cache probe，再扩容其他实例。
5. 验证 `/healthz`、discovery、登录、授权码/PKCE 和实际 cache 业务路径；外部验证未完成时不要标记为完整集成通过。

容器运行时监听 `8080`。示例仍使用占位符：

```bash
docker run --rm -p 8080:8080 -e Database=mysql -e 'ConnectionStrings__DefaultConnection=Server=<mysql-host>;Port=3306;Database=<db>;User ID=<user>;Password=<password>;' <image>:<tag>
```

回滚时应优先停止新版本，恢复上一版本镜像及其原 provider 的 `Database`/`DefaultConnection`，并确认旧版本能读取保留的数据库 schema。不要把旧版本直接指向新 provider 数据库，也不要在生产环境未经备份执行 EF `Down` migration。provider 切换不会搬迁数据；cache 是临时状态，切换后可能需要重新登录、重新同意授权或重新生成验证码。

## 已知告警与待后续安全项

本次定向测试 restore 仍出现既有 `NU1903` 高严重性告警，未在本任务中升级依赖或宣称已修复：

| 包 | 版本 | 告警 |
|---|---:|---|
| `SQLitePCLRaw.lib.e_sqlite3` | `2.1.11` | 已知高严重性漏洞 `GHSA-2m69-gcr7-jv3q` |
| `System.Security.Cryptography.Xml` | `10.0.7` | restore 报告多个已知高严重性 GHSA advisory |

后续可用以下命令重新确认完整依赖树：

```bash
dotnet list tests/OpenIddictUI.Tests/OpenIddictUI.Tests.csproj package --vulnerable --include-transitive
```

`DatabaseStartup.cs` 对 MySQL provider 的 `DbException` 会包装为较短的外层错误，外层 `Message`/`ToString()` 测试不会直接输出连接密码；但当前实现仍把原始异常保留在 `InnerException`（见 `src/OpenIddictUI/Data/DatabaseStartup.cs:91` 和 `:256`）。通用异常序列化、开发者异常页或会遍历 `InnerException` 的日志管道仍可能暴露敏感连接信息。该旁路属于待后续修复项，本阶段不宣称已修复；生产环境必须关闭详细异常输出并限制启动日志访问。
