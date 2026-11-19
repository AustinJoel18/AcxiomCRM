using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AcxiomCRM.Web.Data;
using AcxiomCRM.Web.Infrastructure;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.Validation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
var env = builder.Environment;
var isDevLike = env.IsDevelopment() || env.IsEnvironment("Testing");

// ---------------------------------------------------------------- Configuration
builder.Services.Configure<AppOptions>(config.GetSection(AppOptions.Section));
builder.Services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));
builder.Services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
builder.Services.Configure<CrmValidationOptions>(config.GetSection(CrmValidationOptions.Section));
builder.Services.Configure<SeedOptions>(config.GetSection(SeedOptions.Section));
var security = config.GetSection(SecurityOptions.Section).Get<SecurityOptions>() ?? new SecurityOptions();
var appOptions = config.GetSection(AppOptions.Section).Get<AppOptions>() ?? new AppOptions();

// ---------------------------------------------------------------- Data access
var connectionString = ResolveSqlite(config.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured."), env.ContentRootPath);
builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connectionString));

// ---------------------------------------------------------------- Cross-cutting services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IBusinessClock, BusinessClock>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddScoped<IDataScope, DataScopeService>();
builder.Services.AddScoped<IRequestValidator, RequestValidator>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------- Business services
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IOpportunityService, OpportunityService>();
builder.Services.AddScoped<IFollowUpService, FollowUpService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();

// ---------------------------------------------------------------- Identity (hashing, policy, lockout)
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = security.Password.RequiredLength;
        o.Password.RequireDigit = security.Password.RequireDigit;
        o.Password.RequireUppercase = security.Password.RequireUppercase;
        o.Password.RequireLowercase = security.Password.RequireLowercase;
        o.Password.RequireNonAlphanumeric = security.Password.RequireNonAlphanumeric;
        o.Password.RequiredUniqueChars = security.Password.RequiredUniqueChars;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = security.Lockout.MaxFailedAccessAttempts;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(security.Lockout.LockoutMinutes);
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager<AppSignInManager>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddPasswordValidator<PasswordHistoryValidator>()
    .AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(security.SessionRevalidationMinutes));
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<IPasswordHistoryService, PasswordHistoryService>();
builder.Services.AddScoped<AccessDeniedAuditor>();

var securePolicy = isDevLike ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = ApiCookieAntiforgeryFilter.CookieName;
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = securePolicy;
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(security.SessionTimeoutMinutes);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = ctx =>
    {
        if (IsApi(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    o.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (IsApi(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "AcxiomCRM.Csrf";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = securePolicy;
});

// ---------------------------------------------------------------- API authentication (JWT bearer + cookie)
builder.Services.AddSingleton<JwtKeyProvider>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<ITokenRevocationStore, DistributedTokenRevocationStore>();
builder.Services.AddScoped<ApiCookieAntiforgeryFilter>();

builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { })
    .AddPolicyScheme(ApiAuthDefaults.Scheme, "Bearer token or application cookie", o =>
        o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : IdentityConstants.ApplicationScheme);

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtKeyProvider, IOptions<JwtOptions>>((o, keys, jwt) =>
    {
        o.MapInboundClaims = false;
        o.RequireHttpsMetadata = !isDevLike;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Value.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Value.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = keys.Key,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        o.Events = new JwtBearerEvents
        {
            // Reject revoked tokens and tokens of users who were deactivated, locked or had a security change.
            OnTokenValidated = async ctx =>
            {
                var sp = ctx.HttpContext.RequestServices;
                var principal = ctx.Principal!;
                var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
                if (jti is null || await sp.GetRequiredService<ITokenRevocationStore>().IsRevokedAsync(jti))
                {
                    ctx.Fail("Token has been revoked.");
                    return;
                }
                var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await users.FindByIdAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty);
                if (user is null || !user.IsActive || await users.IsLockedOutAsync(user)
                    || principal.FindFirstValue(AppClaimTypes.SecurityStampHash) != SecurityStamp.Hash(user.SecurityStamp))
                    ctx.Fail("Token is no longer valid.");
            }
        };
    });

// ---------------------------------------------------------------- Authorization (roles + policies)
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Policies.CrmUser, p => p.RequireRole(AppRoles.All))
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole(AppRoles.Admin))
    .AddPolicy(Policies.ManagerOrAdmin, p => p.RequireRole(AppRoles.Admin, AppRoles.Manager))
    .AddPolicy(Policies.CanDeleteRecords, p => p.RequireRole(AppRoles.Admin, AppRoles.Manager))
    .AddPolicy(Policies.UserDirectory, p => p.RequireRole(AppRoles.Admin, AppRoles.Manager))
    .AddPolicy(Policies.AuditViewer, p => p.RequireAssertion(ctx =>
        ctx.User.IsInRole(AppRoles.Admin) || (security.ManagerCanViewAudit && ctx.User.IsInRole(AppRoles.Manager))));

// ---------------------------------------------------------------- Rate limiting (authentication endpoints)
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = security.LoginRateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.OnRejected = async (ctx, ct) =>
    {
        var response = ctx.HttpContext.Response;
        response.Headers.RetryAfter = "60";
        if (IsApi(ctx.HttpContext.Request))
        {
            await response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many login attempts. Please wait a minute and try again."
            }, ct);
        }
        else
        {
            response.ContentType = "text/html; charset=utf-8";
            await response.WriteAsync("<!doctype html><title>Too many attempts</title><p style=\"font-family:sans-serif\">Too many login attempts. Please wait a minute and <a href=\"/Account/Login\">try again</a>.</p>", ct);
        }
    };
});

// ---------------------------------------------------------------- MVC + API
builder.Services.AddControllersWithViews(o =>
    {
        o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
        o.Filters.Add<PasswordChangeRequiredFilter>();
        var m = o.ModelBindingMessageProvider;
        m.SetValueMustNotBeNullAccessor(f => $"{f} is required.");
        m.SetAttemptedValueIsInvalidAccessor((v, f) => $"The value '{v}' is not valid for {f}.");
        m.SetValueIsInvalidAccessor(v => $"The value '{v}' is invalid.");
        m.SetValueMustBeANumberAccessor(f => $"{f} must be a number.");
        m.SetMissingKeyOrValueAccessor(() => "A value is required.");
        m.SetNonPropertyAttemptedValueIsInvalidAccessor(v => $"The value '{v}' is not valid.");
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });

builder.Services.AddOpenApi("v1", o =>
{
    o.ShouldInclude = d => d.RelativePath?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true;
    o.AddDocumentTransformer<AcxiomCRM.Web.Infrastructure.OpenApiBearerTransformer>();
});

var app = builder.Build();

// ---------------------------------------------------------------- Database
if (config.GetValue("Database:MigrateOnStartup", true))
    await DbInitializer.InitializeAsync(app.Services);

// ---------------------------------------------------------------- Pipeline
app.UseExceptionHandler("/Home/Error"); // never expose stack traces; API callers get ProblemDetails
if (!isDevLike)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");

if (appOptions.EnableApiDocs)
{
    app.UseSwaggerUI(o =>
    {
        o.SwaggerEndpoint("/openapi/v1.json", "AcxiomCRM API v1");
        o.RoutePrefix = "swagger";
        o.DocumentTitle = "AcxiomCRM API";
    });
}

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets().AllowAnonymous();
if (appOptions.EnableApiDocs) app.MapOpenApi().AllowAnonymous();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}").WithStaticAssets();

app.Run();

static bool IsApi(HttpRequest request) => request.Path.StartsWithSegments("/api");

static string ResolveSqlite(string connectionString, string contentRoot)
{
    var csb = new SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrEmpty(csb.DataSource) && csb.DataSource != ":memory:" && !Path.IsPathRooted(csb.DataSource))
    {
        csb.DataSource = Path.Combine(contentRoot, csb.DataSource);
        Directory.CreateDirectory(Path.GetDirectoryName(csb.DataSource)!);
    }
    return csb.ToString();
}

/// <summary>Entry point marker for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
