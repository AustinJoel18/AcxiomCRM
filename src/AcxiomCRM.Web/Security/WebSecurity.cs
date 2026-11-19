using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AcxiomCRM.Web.Security;

/// <summary>Baseline HTTP security headers incl. a strict Content-Security-Policy (no inline scripts).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string Csp =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; " +
        "connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        if (!context.Request.Path.StartsWithSegments("/swagger"))
            h.ContentSecurityPolicy = Csp;
        return next(context);
    }
}

/// <summary>
/// API endpoints accept either a Bearer token or the browser's auth cookie. Bearer requests are not
/// CSRF-prone, but cookie-authenticated state-changing API calls must carry an anti-forgery header.
/// </summary>
public sealed class ApiCookieAntiforgeryFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter
{
    public const string CookieName = "AcxiomCRM.Auth";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)) return;
        if (request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return;
        if (!request.Cookies.ContainsKey(CookieName)) return;

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Anti-forgery token validation failed.",
                Detail = "Cookie-authenticated API requests must send the X-CSRF-TOKEN header."
            }) { StatusCode = StatusCodes.Status400BadRequest };
    }
}

/// <summary>Redirects users whose password was reset by an administrator to the change-password page.</summary>
public sealed class PasswordChangeRequiredFilter : IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true && user.HasClaim(AppClaimTypes.MustChangePassword, "true")
            && context.ActionDescriptor is ControllerActionDescriptor action
            && action.ControllerTypeInfo.GetCustomAttributes(typeof(ApiControllerAttribute), true).Length == 0
            && !(action.ControllerName == "Account" && action.ActionName is "ChangePassword" or "Logout"))
        {
            context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
            return Task.CompletedTask;
        }
        return next();
    }
}

/// <summary>Writes an audit entry when an authenticated user is denied access to a page.</summary>
public sealed class AccessDeniedAuditor(IAuditService audit)
{
    public Task RecordAsync(string path) =>
        audit.LogAsync(AuditModule.Security, AuditAction.AccessDenied, AuditResult.Failure, details: $"Access denied to {Truncate(path, 200)}");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
