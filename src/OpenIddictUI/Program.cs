using Identity.Sm;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddictUI.Data;
using OpenIddictUI.Extensions;
using OpenIddictUI.Grants;
using OpenIddictUI.Identity;
using OpenIddictUI.Middlewares;
using OpenIddictUI.Options;
using OpenIddictUI.Plugins;
using OpenIddictUI.Sms;
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
        builder.AddEnvironmentSubstitutedAppSettings();

        builder.Configuration.AddJsonFile("openiddict-seed.json", optional: true, reloadOnChange: true);

        builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));
        var config = builder.Configuration;

        builder.Services.Configure<OpenIddictUIOptions>(config.GetSection("OpenIddictUI"));
        builder.Services.Configure<IdentityExtensionOptions>(config.GetSection("IdentityExtension"));
        builder.Services.Configure<IdentityOptions>(config.GetSection("Identity"));
        builder.Services.Configure<CookiePolicyOptions>(config.GetSection("CookiePolicy"));

        var migrationsTable = config.GetValue<string>("OpenIddictUIOptions:MigrationsHistoryTable")
                              ?? "openiddict_migrations_history";

        builder.Services.AddHealthChecks();
        builder.Services.AddDbContextPool<AppDbContext>(options =>
        {
            options.UseNpgsql(config.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsHistoryTable(migrationsTable));
            options.UseOpenIddict();
        });

        builder.Services.AddIdentity<User, IdentityRole>(options =>
            {
                // 使用短名，避免 XML URL 长名
                options.ClaimsIdentity.RoleClaimType = "role";
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        if (bool.TryParse(builder.Configuration["ENABLE_SM3_PASSWORD_HASHER"],
                out var enable) &&
            enable)
        {
            builder.Services.AddSm3PasswordHasher<User>();
        }

        builder.Services.AddAuthentication();

        // 必须在 AddIdentity 之后，不然配置会被覆盖为默认值
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme,
            builder.Configuration.GetSection("ApplicationCookieAuthentication"));
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme,
            builder.Configuration.GetSection("ExternalCookieAuthentication"));
        builder.Services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme,
            builder.Configuration.GetSection("TwoFactorUserIdCookieAuthentication"));

        builder.Services.AddDistributedPostgresCache(options =>
        {
            options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
            options.SchemaName = builder.Configuration.GetValue<string>("PostgresCache:SchemaName", "public");
            options.TableName = builder.Configuration.GetValue<string>("PostgresCache:TableName", "cache");
            options.CreateIfNotExists = builder.Configuration.GetValue("PostgresCache:CreateIfNotExists", true);
            options.UseWAL = builder.Configuration.GetValue("PostgresCache:UseWAL", false);

            var expirationInterval =
                builder.Configuration.GetValue<string>("PostgresCache:ExpiredItemsDeletionInterval");
            if (!string.IsNullOrEmpty(expirationInterval) && TimeSpan.TryParse(expirationInterval, out var interval))
            {
                options.ExpiredItemsDeletionInterval = interval;
            }

            var slidingExpiration = builder.Configuration.GetValue<string>("PostgresCache:DefaultSlidingExpiration");
            if (!string.IsNullOrEmpty(slidingExpiration) && TimeSpan.TryParse(slidingExpiration, out var sliding))
            {
                options.DefaultSlidingExpiration = sliding;
            }
        });
        builder.Services.AddHybridCache();

        // Grant handlers — AddGrant<T> 同时注册 keyed（查找）和非 keyed（枚举）
        builder.Services.AddGrant<PasswordGrantHandler>(PasswordGrantHandler.GrantType);
        builder.Services.AddGrant<PhoneCodeGrantHandler>(PhoneCodeGrantHandler.GrantType);
        builder.Services.AddGrant<AuthorizationCodeGrantHandler>(AuthorizationCodeGrantHandler.GrantType);

        builder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(10);
            options.Cookie.Name = "session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            options.Cookie.IsEssential = true;
        });

        builder.Services.AddOpenIddict()
            .AddCore(options => { options.UseEntityFrameworkCore().UseDbContext<AppDbContext>(); })
            .AddServer(options =>
            {
                options.RegisterScopes("profile", "email", "phone", "address", "roles");
                options.SetAuthorizationEndpointUris("/connect/authorize")
                    .SetTokenEndpointUris("/connect/token")
                    .SetEndSessionEndpointUris("/connect/logout");

                var issuer = config["OpenIddictUI:Issuer"];
                if (!string.IsNullOrEmpty(issuer))
                {
                    var baseUrl = issuer.TrimEnd('/');
                    options.SetIssuer(new Uri(baseUrl));
                    // 修复 discovery document 中的 endpoint URL，设置为外部 issuer
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

                options.AllowAuthorizationCodeFlow()
                    .AllowPasswordFlow()
                    .AllowRefreshTokenFlow();

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
            options.Cookie.Name = "XSRF-TOKEN";
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
        builder.Services.AddControllers();
        builder.Services.AddRouting(options => options.LowercaseUrls = true);

        builder.Services.AddCors(policy => policy
            .AddPolicy("cors",
                p => p.AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowCredentials()));

        builder.Services.AddKeyedSingleton<ISmsSender, AliYunSmsSender>(
            AliYunSmsSender.Name);
        builder.Services.AddKeyedSingleton<ISmsSender, ConsoleSmsSender>(
            ConsoleSmsSender.Name);

        using var startupLoggerFactory = LoggerFactory.Create(b => b.AddConsole());
        PluginLoader.Load(builder, startupLoggerFactory);

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseMiddleware<DecryptRequestMiddleware>();

        app.UseHealthChecks("/healthz");
        app.UseCookiePolicy();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAntiforgery();
        app.UseCors("cors");
        app.UseSession();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.Map("connect/userinfo", async (HttpContext ctx) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                ctx.Response.StatusCode = 401;
                return;
            }

            var claims = ctx.User.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key, g => g.Count() > 1 ? g.Select(c => c.Value).ToArray() : (object)g.First().Value);
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(claims);
        });
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
}