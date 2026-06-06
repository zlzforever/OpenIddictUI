using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Authority"] ?? "http://localhost:5164";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
            // ValidAudiences = builder.Configuration.GetSection("ValidAudiences").Get<string[]>()
            //                  ?? ["api1"],
            ValidateIssuer = false,
            ValidIssuer = options.Authority
        };
        options.RequireHttpsMetadata = false;
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
app.UseAuthorization();
app.MapControllers();

app.Logger.LogInformation("OpenIddictUI.Api starting");

app.Run();