using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenIddictUI.Api;

var builder = WebApplication.CreateBuilder(args);
var authority = builder.Configuration["Authority"] ?? "http://localhost:5164";
if (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
    (authorityUri.Scheme != Uri.UriSchemeHttps && (!builder.Environment.IsDevelopment() || authorityUri.Scheme != Uri.UriSchemeHttp)))
{
    throw new InvalidOperationException("Authority must be HTTPS outside development.");
}
var expectedIssuer = new Uri(authority.TrimEnd('/')).AbsoluteUri;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudiences = builder.Configuration.GetSection("ValidAudiences").Get<string[]>() ?? ["api1"],
            ValidateIssuer = true,
            ValidIssuer = expectedIssuer,
            IssuerValidator = (issuer, _, _) => string.Equals(issuer, expectedIssuer, StringComparison.Ordinal)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException("Invalid issuer."),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            RoleClaimType = "role",
            ValidTypes = ["at+jwt", "application/at+jwt"]
        };
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("api1", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.HasClaim(c =>
                c.Type == "scope" &&
                c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("api1")));
    });
});

builder.Services.AddControllers();
builder.Services.AddCors(policy => policy
    .AddPolicy("cors", p => p.AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowAnyHeader()));

var app = builder.Build();

app.UseCors("cors");
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true &&
        context.User.HasClaim("token_use", "temporary_readonly") &&
        context.GetEndpoint()?.Metadata.GetMetadata<OperationClassificationAttribute>()?.Classification != OperationClassification.ReadOnly)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();

app.Logger.LogInformation("OpenIddictUI.Api starting");

app.Run();
