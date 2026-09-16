using Identity.Sm;
using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.WebUtilities;
using MySqlConnector;
using OpenIddictUI.Data;
using OpenIddictUI.Extensions;
using OpenIddictUI.Grants;
using OpenIddictUI.Identity;
using OpenIddictUI.Middlewares;
using OpenIddictUI.Options;
using OpenIddictUI.Plugins;
using OpenIddictUI.Sms;
using OpenIddictUI.Weixin;
using Serilog;

namespace OpenIddictUI;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var app = CreateWebApplication(args);

        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("OpenIddictUI starting");

        await SeedData.ApplyAsync(app.Services);
        await app.RunAsync();
    }

    public static WebApplication CreateWebApplication(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddSubstitution();
        builder.Configuration.AddJsonFile("openiddict-seed.json", optional: true, reloadOnChange: true);
        builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

        var config = builder.Configuration;
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ApplicationException("DefaultConnection is required");
        }

        var openiddictOptionSection = config.GetSection("OpenIddict");
        builder.Services.Configure<OpenIddictOptions>(openiddictOptionSection);
        builder.Services.Configure<IdentityExtensionOptions>(config.GetSection("IdentityExtension"));
        builder.Services.Configure<IdentityOptions>(config.GetSection("Identity"));
        builder.Services.Configure<CookiePolicyOptions>(config.GetSection("CookiePolicy"));
        builder.Services.Configure<GlobalOptions>(config);

        var openiddictOptions = openiddictOptionSection.Exists()
            ? openiddictOptionSection.Get<OpenIddictOptions>() ?? new OpenIddictOptions()
            : new OpenIddictOptions();
        var migrationsTable = string.IsNullOrWhiteSpace(openiddictOptions.MigrationsHistoryTable)
            ? "openiddict_migrations_history"
            : openiddictOptions.MigrationsHistoryTable;

        var databaseProvider = DatabaseProviderResolver.Resolve(config);
        // MySqlCacheSettings? mySqlCacheSettings = null;
        // if (databaseProvider == DatabaseProvider.MySql)
        // {
        //     mySqlCacheSettings = MySqlCacheSettings.FromConfiguration(config);
        //     builder.Services.AddSingleton(mySqlCacheSettings);
        // }

        builder.Services.AddHealthChecks();

        // builder.Services.Configure<ForwardedHeadersOptions>(options =>
        // {
        //     options.ForwardedHeaders =
        //         ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto;
        //
        //     // Dapr sidecar 通常与应用处于同一 Pod，默认信任本机回环地址；跨节点部署时通过环境变量指定。
        //     var trustedProxyIp = Environment.GetEnvironmentVariable("TRUSTED_PROXY_IP") ?? "127.0.0.1";
        //     if (!IPAddress.TryParse(trustedProxyIp, out var proxyIp))
        //     {
        //         throw new InvalidOperationException($"TRUSTED_PROXY_IP 不是有效的 IP 地址：{trustedProxyIp}");
        //     }
        //
        //     options.KnownProxies.Add(proxyIp);
        // });

        RegisterDbContext(builder, databaseProvider, connectionString, migrationsTable);

        RegisterCache(builder, databaseProvider, connectionString);

        var identityBuilder = builder.Services.AddIdentity<User, IdentityRole>(options =>
        {
            // 使用短名，避免 XML URL 长名
            options.ClaimsIdentity.RoleClaimType = "role";
        });
        if (databaseProvider == DatabaseProvider.Postgres)
        {
            identityBuilder.AddEntityFrameworkStores<AppDbContext>();
        }
        else
        {
            identityBuilder.AddEntityFrameworkStores<MySqlAppDbContext>();
        }

        identityBuilder.AddDefaultTokenProviders();

        if (bool.TryParse(builder.Configuration["ENABLE_SM3_PASSWORD_HASHER"],
                out var enable) &&
            enable)
        {
            builder.Services.AddSm3PasswordHasher<User>();
        }

        var authenticationBuilder = builder.Services.AddAuthentication();
        var loginProviders = builder.Configuration.Get<GlobalOptions>()?.AuthenticationSchemes;
        if (loginProviders != null &&
            loginProviders.Any(provider =>
                string.Equals(provider.Trim(), Util.LoginProviderWeixin, StringComparison.OrdinalIgnoreCase)))
        {
            var weixinSettings = builder.Configuration.GetSection("Weixin").Get<WeixinOptions>();
            if (weixinSettings == null)
            {
                throw new InvalidOperationException("WeixinOptions is required");
            }

            authenticationBuilder.AddWeixin(options =>
            {
                options.ClientId = weixinSettings.AppId;
                options.ClientSecret = weixinSettings.AppSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;

                var configuredRedirectBase = config["WEIXIN_REDIRECT_BASE"];
                if (string.IsNullOrWhiteSpace(configuredRedirectBase))
                {
                    configuredRedirectBase = weixinSettings.RedirectBaseUri;
                }

                Uri? redirectBaseUri = null;
                if (!string.IsNullOrWhiteSpace(configuredRedirectBase) &&
                    (!Uri.TryCreate(configuredRedirectBase, UriKind.Absolute, out redirectBaseUri) ||
                     (redirectBaseUri.Scheme != Uri.UriSchemeHttp && redirectBaseUri.Scheme != Uri.UriSchemeHttps)))
                {
                    throw new InvalidOperationException(
                        "Weixin:RedirectUri/WEIXIN_REDIRECT_URI 必须是 http 或 https 的绝对地址");
                }

                var publicIssuer = Uri.TryCreate(openiddictOptions.Issuer, UriKind.Absolute, out var issuerUri)
                    ? issuerUri
                    : null;

                if (Util.ServiceProvider == null)
                {
                    throw new ArgumentNullException(nameof(Util.ServiceProvider), "ServiceProvider is not initialized");
                }

                var distributedCache = Util.ServiceProvider.GetRequiredService<IDistributedCache>();
                // 关键：注入自定义StateDataFormat
                options.StateDataFormat = new CachedStateDataFormat(distributedCache);

                // 网站应用固定scope snsapi_login，支持PC快速登录
                options.Scope.Add("snsapi_login");

                // 回调路径，和微信开放平台回调域名匹配
                options.CallbackPath = "/openid/weixin_login_callback";
                options.Events.OnRedirectToAuthorizationEndpoint = context =>
                {
                    if (redirectBaseUri != null)
                    {
                        context.RedirectUri = ReplaceRedirectOrigin(
                            context.RedirectUri, redirectBaseUri);
                    }
                    else if (publicIssuer != null)
                    {
                        // 未配置精确回调地址时，只使用公网 Issuer 替换内部请求的 scheme/host，保留回调路径。
                        context.RedirectUri = ReplaceRedirectOrigin(context.RedirectUri, publicIssuer);
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    var returnUrl = context.Properties?.Items.TryGetValue(Util.ExternalLoginReturnUrl,
                        out var value) == true
                        ? value
                        : null;
                    var loginUrl = string.IsNullOrEmpty(openiddictOptions.Issuer)
                        ? "/account/login"
                        : $"{openiddictOptions.Issuer.TrimEnd('/')}/account/login";
                    var query = $"error=external_login_failed";
                    if (!string.IsNullOrWhiteSpace(returnUrl))
                    {
                        query += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
                    }

                    context.Response.Redirect($"{loginUrl}?{query}");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        // 必须在 AddIdentity 之后，不然配置会被覆盖为默认值
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
            builder.Configuration.GetSection("ApplicationCookieAuthentication"));
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme,
            builder.Configuration.GetSection("ExternalCookieAuthentication"));
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme,
            builder.Configuration.GetSection("TwoFactorUserIdCookieAuthentication"));

        // Grant handlers — AddGrant<T> 同时注册 keyed（查找）和非 keyed（枚举）
        builder.Services.AddGrant<PasswordGrantHandler>(PasswordGrantHandler.GrantType);
        builder.Services.AddGrant<PhoneCodeGrantHandler>(PhoneCodeGrantHandler.GrantType);
        builder.Services.AddGrant<AuthorizationCodeGrantHandler>(AuthorizationCodeGrantHandler.GrantType);

        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                if (databaseProvider == DatabaseProvider.Postgres)
                {
                    options.UseEntityFrameworkCore().UseDbContext<AppDbContext>();
                }
                else
                {
                    options.UseEntityFrameworkCore().UseDbContext<MySqlAppDbContext>();
                }
            })
            .AddServer(options =>
            {
                options.RegisterScopes("profile", "email", "phone", "address", "roles");
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/logout")
                    .SetUserInfoEndpointUris("/connect/userinfo");

                var issuer = openiddictOptions.Issuer;
                if (!string.IsNullOrEmpty(issuer))
                {
                    var baseUrl = issuer.TrimEnd('/');
                    options.SetIssuer(new Uri(baseUrl));
                    // 只在 discovery document 事件中覆盖 endpoint URL
                    options
                        .AddEventHandler<
                            OpenIddict.Server.OpenIddictServerEvents.ApplyConfigurationResponseContext>(e =>
                            e.UseInlineHandler(context =>
                            {
                                context.Response["issuer"] = new OpenIddict.Abstractions.OpenIddictParameter(baseUrl);
                                context.Response["authorization_endpoint"] =
                                    new OpenIddict.Abstractions.OpenIddictParameter($"{baseUrl}/connect/authorize");
                                context.Response["token_endpoint"] =
                                    new OpenIddict.Abstractions.OpenIddictParameter($"{baseUrl}/connect/token");
                                context.Response["end_session_endpoint"] =
                                    new OpenIddict.Abstractions.OpenIddictParameter($"{baseUrl}/connect/logout");
                                context.Response["jwks_uri"] =
                                    new OpenIddict.Abstractions.OpenIddictParameter($"{baseUrl}/.well-known/jwks");
                                context.Response["userinfo_endpoint"] =
                                    new OpenIddict.Abstractions.OpenIddictParameter($"{baseUrl}/connect/userinfo");
                                return default;
                            }));
                    // 一次性设置 returnUrl 白名单前缀，后续请求不再拼接
                    Util.AuthorizePrefix = string.IsNullOrEmpty(issuer)
                        ? "/connect/authorize?"
                        : $"{baseUrl}/connect/authorize?";
                }

                options.AllowAuthorizationCodeFlow();
                options.AllowRefreshTokenFlow();

                if (loginProviders.Contains(Util.LoginProviderPassword, StringComparer.OrdinalIgnoreCase))
                {
                    options.AllowPasswordFlow();
                }

                if (loginProviders.Contains(Util.LoginProviderSms, StringComparer.OrdinalIgnoreCase))
                {
                    options.AllowCustomFlow(PhoneCodeGrantHandler.GrantType);
                }

                options.AddSigningCredential();
                options.DisableAccessTokenEncryption();

                options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableTokenEndpointPassthrough()
                    .EnableEndSessionEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough()
                    .DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "X-XSRF-TOKEN";
            // options.Cookie.SameSite = SameSiteMode.Lax;
        });


        builder.Services.AddControllers();
        var type = Type.GetType(
            "Microsoft.AspNetCore.Mvc.ViewFeatures.Filters.AutoValidateAntiforgeryTokenAuthorizationFilter, Microsoft.AspNetCore.Mvc.ViewFeatures");
        if (type != null)
        {
            builder.Services.AddSingleton(type);
        }

        builder.Services.AddRouting(options => options.LowercaseUrls = true);

        builder.Services.AddCors(policy => policy
            .AddPolicy("cors",
                p => p.AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowCredentials()));

        builder.Services.AddKeyedSingleton<ISmsSender, AliYunSmsSender>(
            AliYunSmsSender.Name);
        builder.Services.AddKeyedSingleton<ISmsSender, ConsoleSmsSender>(
            ConsoleSmsSender.Name);
        builder.Services.AddSingleton<ISmsManager, SmsManager>();
        builder.Services.Configure<AliYunOptions>(config.GetSection("AliYun"));

        using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
        PluginLoader.Load(builder, startupLoggerFactory);

        var app = builder.Build();

        // 读取反向代理使用的二级路径；容器 Production 环境同样需要该前缀。
        var basePath = (Environment.GetEnvironmentVariable("BASE_PATH") ?? "").Trim().Trim('/');
        Util.BasePath = string.IsNullOrEmpty(basePath) ? "" : $"/{basePath}";
        Util.ServiceProvider = app.Services;

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // 【必须在这里，路由之前】否则 OAuth redirect_uri 会读取到 Dapr 的内部地址。
        // app.UseForwardedHeaders();
        app.UseMiddleware<DecryptRequestMiddleware>();

        app.UseHealthChecks("/healthz");
        app.UseCookiePolicy();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();
        app.UseCors("cors");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapFallbackToFile("index.html");

        var inDapr = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAPR_HTTP_PORT"));
        if (inDapr)
        {
            app.UseCloudEvents();
            app.MapSubscribeHandler();
        }

        PluginLoader.Use(app, app.Services.GetRequiredService<ILoggerFactory>());

        return app;
    }

    private static string ReplaceQueryValue(string uri, string key, string value)
    {
        var uriBuilder = new UriBuilder(uri);
        var query = QueryHelpers.ParseQuery(uriBuilder.Query)
            .ToDictionary(item => item.Key, item => (string?)item.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        query[key] = value;
        uriBuilder.Query = QueryHelpers.AddQueryString(string.Empty, query).TrimStart('?');
        return uriBuilder.Uri.AbsoluteUri;
    }

    private static string ReplaceRedirectOrigin(string authorizationUri, Uri publicIssuer)
    {
        var uriBuilder = new UriBuilder(authorizationUri);
        var query = QueryHelpers.ParseQuery(uriBuilder.Query)
            .ToDictionary(item => item.Key, item => (string?)item.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        if (!query.TryGetValue("redirect_uri", out var redirectUri) ||
            !Uri.TryCreate(redirectUri, UriKind.Absolute, out var callbackUri))
        {
            return authorizationUri;
        }

        var callbackBuilder = new UriBuilder(callbackUri)
        {
            Scheme = publicIssuer.Scheme,
            Host = publicIssuer.Host,
            Port = publicIssuer.IsDefaultPort ? -1 : publicIssuer.Port
        };
        query["redirect_uri"] = callbackBuilder.Uri.AbsoluteUri;
        uriBuilder.Query = QueryHelpers.AddQueryString(string.Empty, query).TrimStart('?');
        return uriBuilder.Uri.AbsoluteUri;
    }

    private static bool HasLocalRedirectOrigin(string authorizationUri)
    {
        var uriBuilder = new UriBuilder(authorizationUri);
        var query = QueryHelpers.ParseQuery(uriBuilder.Query);
        if (!query.TryGetValue("redirect_uri", out var redirectUri) ||
            !Uri.TryCreate(redirectUri.ToString(), UriKind.Absolute, out var callbackUri))
        {
            return false;
        }

        return callbackUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(callbackUri.Host, out var ip) && IPAddress.IsLoopback(ip));
    }

    private static void RegisterCache(WebApplicationBuilder builder, DatabaseProvider databaseProvider,
        string connectionString)
    {
        if (databaseProvider == DatabaseProvider.Postgres)
        {
            builder.Services.AddDistributedPostgresCache(options =>
            {
                options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                options.SchemaName = builder.Configuration.GetValue<string>("PostgresCache:SchemaName", "public");
                options.TableName =
                    builder.Configuration.GetValue<string>("PostgresCache:TableName", "openiddict_cache_entries");
                options.CreateIfNotExists = builder.Configuration.GetValue("PostgresCache:CreateIfNotExists", true);
                options.UseWAL = builder.Configuration.GetValue("PostgresCache:UseWAL", false);

                var expirationInterval =
                    builder.Configuration.GetValue<string>("PostgresCache:ExpiredItemsDeletionInterval");
                if (!string.IsNullOrEmpty(expirationInterval) &&
                    TimeSpan.TryParse(expirationInterval, out var interval))
                {
                    options.ExpiredItemsDeletionInterval = interval;
                }

                var slidingExpiration =
                    builder.Configuration.GetValue<string>("PostgresCache:DefaultSlidingExpiration");
                if (!string.IsNullOrEmpty(slidingExpiration) && TimeSpan.TryParse(slidingExpiration, out var sliding))
                {
                    options.DefaultSlidingExpiration = sliding;
                }
            });
        }
        else
        {
            builder.Services.AddDistributedMySqlCache(options =>
            {
                var connectionBuilder = new MySqlConnectionStringBuilder(connectionString);

                var databaseName = connectionBuilder.Database;

                options.ConnectionString = connectionString;
                options.SchemaName = databaseName;
                options.TableName =
                    builder.Configuration.GetValue<string>("MySqlCache:TableName", "openiddict_cache_entries");

                var expirationInterval =
                    builder.Configuration.GetValue<string>("MySqlCache:ExpiredItemsDeletionInterval");
                if (!string.IsNullOrEmpty(expirationInterval) &&
                    TimeSpan.TryParse(expirationInterval, out var interval))
                {
                    options.ExpiredItemsDeletionInterval = interval;
                }

                var slidingExpiration =
                    builder.Configuration.GetValue<string>("MySqlCache:DefaultSlidingExpiration");
                if (!string.IsNullOrEmpty(slidingExpiration) && TimeSpan.TryParse(slidingExpiration, out var sliding))
                {
                    options.DefaultSlidingExpiration = sliding;
                }

                var createIfNotExists = builder.Configuration.GetValue("MySqlCache:CreateIfNotExists", true);
                if (createIfNotExists)
                {
                    DatabaseStartup.InitializeMySql(options);
                }
            });
        }

        builder.Services.AddHybridCache();
    }

    private static void RegisterDbContext(WebApplicationBuilder builder, DatabaseProvider databaseProvider,
        string connectionString, string migrationsTable)
    {
        if (databaseProvider == DatabaseProvider.Postgres)
        {
            builder.Services.AddDbContextPool<AppDbContext>(options =>
            {
                options.UseNpgsql(connectionString,
                    npgsql => npgsql
                        .MigrationsHistoryTable(migrationsTable)
                        .MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
                options.UseOpenIddict();
            });
        }
        else
        {
            builder.Services.AddDbContextPool<MySqlAppDbContext>(options =>
            {
                options.UseMySql(
                    connectionString,
                    ServerVersion.AutoDetect(connectionString),
                    mysql => mysql
                        .MigrationsHistoryTable(migrationsTable)
                        .MigrationsAssembly(typeof(MySqlAppDbContext).Assembly.GetName().Name));
                options.UseOpenIddict();
            });
            // 保留既有 AppDbContext 解析路径，SeedData 和业务调用方无需分叉。
            builder.Services.AddScoped<AppDbContext>(services =>
                services.GetRequiredService<MySqlAppDbContext>());
        }
    }
}