using System.Security.Claims;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using AcxiomCRM.Web.Security;
using AcxiomCRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AcxiomCRM.Web.Controllers.Api;

/// <summary>
/// Base for all REST controllers: Bearer-or-cookie authentication, CRM-role authorization on every endpoint,
/// automatic 400 for invalid payloads, and consistent RFC 7807 error bodies (no stack traces).
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = ApiAuthDefaults.Scheme, Policy = Policies.CrmUser)]
[IgnoreAntiforgeryToken]
[TypeFilter(typeof(ApiCookieAntiforgeryFilter))]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
public abstract class ApiControllerBase : ControllerBase
{
    protected ActionResult Failure(ServiceResult result)
    {
        switch (result.Status)
        {
            case ResultStatus.NotFound:
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "The requested resource was not found.");
            case ResultStatus.Forbidden:
                return Problem(statusCode: StatusCodes.Status403Forbidden, title: result.Message ?? "You do not have permission to access this resource.");
            case ResultStatus.Conflict:
                return Conflict(new ValidationProblemDetails(result.Errors.Where(e => e.Key != "").ToDictionary(e => e.Key, e => e.Value.ToArray()))
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = result.Message ?? "The request conflicts with an existing record."
                });
            default:
                foreach (var (key, messages) in result.Errors)
                    foreach (var m in messages) ModelState.AddModelError(key, m);
                return ValidationProblem(ModelState);
        }
    }
}

/// <summary>Authentication: issue and revoke JWT access tokens.</summary>
[Route("api/auth")]
public sealed class AuthApiController(LoginService login, JwtTokenService tokens, ITokenRevocationStore revocations,
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, IAuditService audit) : ApiControllerBase
{
    /// <summary>Authenticate with username/email and password. Rate limited; repeated failures lock the account.</summary>
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var outcome = await login.LoginAsync(request.UserNameOrEmail!, request.Password!, issueCookie: false);
        switch (outcome.Status)
        {
            case LoginStatus.LockedOut:
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Account is locked because of repeated failed sign-in attempts.",
                    detail: outcome.LockoutEnd is { } end ? $"Try again after {end:u}." : null);
            case LoginStatus.InvalidCredentials or LoginStatus.Inactive:
                return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid username/email or password.");
        }

        var user = outcome.User!;
        if (user.MustChangePassword)
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Password change required. Sign in to the web application to set a new password.");

        var roles = await users.GetRolesAsync(user);
        var (token, expires, _) = tokens.Create(user, roles);
        return Ok(new LoginResponse
        {
            AccessToken = token,
            ExpiresAt = expires,
            User = new UserSummaryDto(user.Id, user.FullName, user.Email ?? "", roles.FirstOrDefault())
        });
    }

    /// <summary>Revoke the current bearer token (or end the cookie session).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (jti is not null)
        {
            var exp = long.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Exp), out var e)
                ? DateTimeOffset.FromUnixTimeSeconds(e).UtcDateTime
                : DateTime.UtcNow.AddHours(2);
            await revocations.RevokeAsync(jti, exp);
        }
        else
        {
            await signIn.SignOutAsync();
        }
        await audit.LogAsync(AuditModule.Authentication, AuditAction.Logout, details: jti is null ? "API cookie sign-out." : "API token revoked.");
        return NoContent();
    }

    /// <summary>The authenticated user (no security-sensitive fields).</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserSummaryDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UserSummaryDto>> Me()
    {
        var user = await users.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "");
        if (user is null) return Unauthorized();
        var roles = await users.GetRolesAsync(user);
        return new UserSummaryDto(user.Id, user.FullName, user.Email ?? "", roles.FirstOrDefault());
    }
}

/// <summary>Customers (scoped: Admin all, Manager team, Sales Executive own).</summary>
[Route("api/customers")]
public sealed class CustomersApiController(ICustomerService customers) : ApiControllerBase
{
    /// <summary>List/search customers. Sort keys: code, name, company, email, status, owner, created.</summary>
    [HttpGet]
    public Task<PagedResult<CustomerDto>> Get([FromQuery] CustomerQuery query, CancellationToken ct) => customers.SearchAsync(query, ct);

    [HttpGet("{id:int}", Name = "GetCustomer")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> Get(int id, CancellationToken ct)
    {
        var r = await customers.GetAsync(id, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Post(CustomerSaveRequest request, CancellationToken ct)
    {
        var r = await customers.CreateAsync(request, ct);
        return r.Succeeded ? CreatedAtRoute("GetCustomer", new { id = r.Value!.CustomerId }, r.Value) : Failure(r);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Put(int id, CustomerSaveRequest request, CancellationToken ct)
    {
        var r = await customers.UpdateAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    /// <summary>Deactivate (soft-delete) a customer. Admin/Manager only.</summary>
    [HttpDelete("{id:int}"), Authorize(Policy = Policies.CanDeleteRecords)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var r = await customers.DeleteAsync(id, ct);
        return r.Succeeded ? NoContent() : Failure(r);
    }
}

[Route("api/leads")]
public sealed class LeadsApiController(ILeadService leads) : ApiControllerBase
{
    /// <summary>List/search leads. Sort keys: code, name, company, status, source, priority, value, owner, created.</summary>
    [HttpGet]
    public Task<PagedResult<LeadDto>> Get([FromQuery] LeadQuery query, CancellationToken ct) => leads.SearchAsync(query, ct);

    [HttpGet("{id:int}", Name = "GetLead")]
    [ProducesResponseType(typeof(LeadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LeadDto>> Get(int id, CancellationToken ct)
    {
        var r = await leads.GetAsync(id, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpPost]
    [ProducesResponseType(typeof(LeadDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LeadDto>> Post(LeadSaveRequest request, CancellationToken ct)
    {
        var r = await leads.CreateAsync(request, ct);
        return r.Succeeded ? CreatedAtRoute("GetLead", new { id = r.Value!.LeadId }, r.Value) : Failure(r);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(LeadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LeadDto>> Put(int id, LeadSaveRequest request, CancellationToken ct)
    {
        var r = await leads.UpdateAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    /// <summary>Change lead status according to the workflow (invalid transitions return 400).</summary>
    [HttpPost("{id:int}/status")]
    [ProducesResponseType(typeof(LeadDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<LeadDto>> ChangeStatus(int id, LeadStatusChangeRequest request, CancellationToken ct)
    {
        var r = await leads.ChangeStatusAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    /// <summary>Convert a Qualified lead into a customer (new or existing) and optionally an opportunity.</summary>
    [HttpPost("{id:int}/convert")]
    [ProducesResponseType(typeof(LeadConvertResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LeadConvertResult>> Convert(int id, LeadConvertRequest request, CancellationToken ct)
    {
        var r = await leads.ConvertAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpDelete("{id:int}"), Authorize(Policy = Policies.CanDeleteRecords)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var r = await leads.DeleteAsync(id, ct);
        return r.Succeeded ? NoContent() : Failure(r);
    }
}

[Route("api/opportunities")]
public sealed class OpportunitiesApiController(IOpportunityService opportunities) : ApiControllerBase
{
    /// <summary>List/search opportunities. Sort keys: name, customer, stage, amount, probability, close, owner, created.</summary>
    [HttpGet]
    public Task<PagedResult<OpportunityDto>> Get([FromQuery] OpportunityQuery query, CancellationToken ct) => opportunities.SearchAsync(query, ct);

    [HttpGet("{id:int}", Name = "GetOpportunity")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OpportunityDto>> Get(int id, CancellationToken ct)
    {
        var r = await opportunities.GetAsync(id, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    /// <summary>Create an opportunity. Amount &gt; 0 and a future close date are required while active; probability 0–100.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpportunityDto>> Post(OpportunitySaveRequest request, CancellationToken ct)
    {
        var r = await opportunities.CreateAsync(request, ct);
        return r.Succeeded ? CreatedAtRoute("GetOpportunity", new { id = r.Value!.OpportunityId }, r.Value) : Failure(r);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpportunityDto>> Put(int id, OpportunitySaveRequest request, CancellationToken ct)
    {
        var r = await opportunities.UpdateAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpPost("{id:int}/stage")]
    [ProducesResponseType(typeof(OpportunityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpportunityDto>> ChangeStage(int id, OpportunityStageChangeRequest request, CancellationToken ct)
    {
        var r = await opportunities.ChangeStageAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpDelete("{id:int}"), Authorize(Policy = Policies.CanDeleteRecords)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var r = await opportunities.DeleteAsync(id, ct);
        return r.Succeeded ? NoContent() : Failure(r);
    }
}

[Route("api/followups")]
public sealed class FollowUpsApiController(IFollowUpService followUps) : ApiControllerBase
{
    /// <summary>List follow-ups. view: pending | overdue | today | upcoming.</summary>
    [HttpGet]
    public Task<PagedResult<FollowUpDto>> Get([FromQuery] FollowUpQuery query, CancellationToken ct) => followUps.SearchAsync(query, ct);

    [HttpGet("{id:int}", Name = "GetFollowUp")]
    [ProducesResponseType(typeof(FollowUpDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FollowUpDto>> Get(int id, CancellationToken ct)
    {
        var r = await followUps.GetAsync(id, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    /// <summary>Schedule a follow-up (date cannot be earlier than today).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(FollowUpDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FollowUpDto>> Post(FollowUpCreateRequest request, CancellationToken ct)
    {
        var r = await followUps.CreateAsync(request, ct);
        return r.Succeeded ? CreatedAtRoute("GetFollowUp", new { id = r.Value!.FollowUpId }, r.Value) : Failure(r);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(FollowUpDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FollowUpDto>> Put(int id, FollowUpEditRequest request, CancellationToken ct)
    {
        var r = await followUps.UpdateAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpPost("{id:int}/complete")]
    [ProducesResponseType(typeof(FollowUpDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FollowUpDto>> Complete(int id, FollowUpCompleteRequest request, CancellationToken ct)
    {
        var r = await followUps.CompleteAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpPost("{id:int}/reschedule")]
    [ProducesResponseType(typeof(FollowUpDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FollowUpDto>> Reschedule(int id, FollowUpRescheduleRequest request, CancellationToken ct)
    {
        var r = await followUps.RescheduleAsync(id, request, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpPost("{id:int}/missed")]
    [ProducesResponseType(typeof(FollowUpDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FollowUpDto>> Missed(int id, CancellationToken ct)
    {
        var r = await followUps.MarkMissedAsync(id, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(FollowUpDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<FollowUpDto>> Cancel(int id, CancellationToken ct)
    {
        var r = await followUps.CancelAsync(id, ct);
        return r.Succeeded ? r.Value! : Failure(r);
    }
}

[Route("api/reports")]
public sealed class ReportsApiController(IOpportunityService opportunities, IDashboardService dashboard) : ApiControllerBase
{
    /// <summary>Stage-wise and owner-wise pipeline. Admin: all; Manager: team; Sales Executive: own (scoped view).</summary>
    [HttpGet("pipeline")]
    [ProducesResponseType(typeof(PipelineReportDto), StatusCodes.Status200OK)]
    public Task<PipelineReportDto> Pipeline(string? ownerId, DateTime? from, DateTime? to, CancellationToken ct) =>
        opportunities.PipelineAsync(ownerId, from, to, ct);

    /// <summary>Dashboard KPIs and chart series for the caller's scope.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardData), StatusCodes.Status200OK)]
    public Task<DashboardData> Dashboard([FromQuery] DateRangeFilter filter, CancellationToken ct) => dashboard.GetAsync(filter, ct);
}
