using System.Text;
using ApiSentinel.Api;
using ApiSentinel.Api.Services;
using ApiSentinel.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Hangfire;
using System.Threading.RateLimiting;

namespace ApiSentinel.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSentinelServices(this IServiceCollection services, IConfiguration config)
    {
        var connection = config.GetConnectionString("Default") ?? "Server=(localdb)\\MSSQLLocalDB;Database=ApiSentinel;Trusted_Connection=True;TrustServerCertificate=True";
        var key = config["Jwt:Key"] ?? throw new InvalidOperationException("Configure Jwt__Key with at least 32 random characters.");
        services.AddDbContext<SentinelDb>(o => o.UseSqlServer(connection, sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<HttpRequestExecutor>();
        services.AddScoped<MonitoringJob>();
        services.AddScoped<MonitoringScheduler>();
        services.AddScoped<CredentialProtector>();
        services.AddScoped<EnvironmentResolver>();
        services.AddScoped<GroqAiService>();
        services.AddHttpClient("target", c => c.Timeout = Timeout.InfiniteTimeSpan).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, ConnectCallback = OutboundTargetPolicy.ConnectAsync });
        services.AddHttpClient("groq", c => { c.BaseAddress = new Uri("https://api.groq.com/openai/v1/"); c.Timeout = TimeSpan.FromSeconds(25); });
        services.AddHangfire(h => h.SetDataCompatibilityLevel(CompatibilityLevel.Version_180).UseSimpleAssemblyNameTypeSerializer().UseRecommendedSerializerSettings().UseSqlServerStorage(connection));
        services.AddHangfireServer(options => options.WorkerCount = 4);
        services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(config["FrontendOrigin"] ?? "http://localhost:5173").AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new()
            {
                ValidateIssuer = true, ValidIssuer = "api-sentinel", ValidateAudience = true, ValidAudience = "api-sentinel-client",
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
            };
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var idClaim = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    var versionClaim = context.Principal?.FindFirst("token_version")?.Value;
                    if (!int.TryParse(idClaim, out var userId) || !int.TryParse(versionClaim, out var tokenVersion)) { context.Fail("Invalid token."); return; }
                    var db = context.HttpContext.RequestServices.GetRequiredService<SentinelDb>();
                    var currentVersion = await db.Users.Where(user => user.Id == userId).Select(user => (int?)user.TokenVersion).SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                    if (currentVersion is null || currentVersion != tokenVersion) context.Fail("Token revoked.");
                }
            };
        });
        services.AddAuthorization(); services.AddEndpointsApiExplorer(); services.AddSwaggerGen();
        var dataProtection = services.AddDataProtection().SetApplicationName("ApiSentinel");
        var keyRingPath = config["DataProtection:KeysPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath)) dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetConcurrencyLimiter(
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new ConcurrencyLimiterOptions { PermitLimit = 8, QueueLimit = 0, QueueProcessingOrder = QueueProcessingOrder.OldestFirst }));
            options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.AddPolicy("outbound", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
            options.AddPolicy("ai", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
        });
        return services;
    }
}