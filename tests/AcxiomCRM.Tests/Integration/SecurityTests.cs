using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Tests.Integration;

/// <summary>Authentication, password security, lockout, authorization and anti-forgery (checklists §13/§14, §17.19).</summary>
public sealed class SecurityTests(CrmWebFactory factory) : IClassFixture<CrmWebFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Customers")]
    [InlineData("/Reports/Pipeline")]
    [InlineData("/Users")]
    public async Task Protected_pages_redirect_anonymous_users_to_login(string path)
    {
        var response = await factory.Browser().GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/Login", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Api_requires_authentication_and_returns_problem_details()
    {
        var response = await factory.CreateClient().GetAsync("/api/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Valid_login_reaches_dashboard_and_invalid_login_is_rejected()
    {
        var client = await factory.SignedInBrowserAsync(Users.Sales);
        var dashboard = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("dashboard-data", html);
        Assert.Contains("leadStatusChart", html);

        var bad = factory.Browser();
        var token = await CrmWebFactory.AntiforgeryTokenAsync(bad, "/Account/Login");
        var response = await bad.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserNameOrEmail"] = Users.Sales2, ["Password"] = "Wrong#Pass1", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Invalid login attempt", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logout_terminates_the_session()
    {
        var client = await factory.SignedInBrowserAsync(Users.Sales2);
        var token = await CrmWebFactory.AntiforgeryTokenAsync(client, "/Customers/Create");
        var logout = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        var after = await client.GetAsync("/Customers");
        Assert.Equal(HttpStatusCode.Redirect, after.StatusCode);
        Assert.StartsWith("/Account/Login", after.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Repeated_failed_logins_lock_the_account_and_are_audited()
    {
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = Users.Sales4, password = "Wrong#Pass1" })).StatusCode);

        // Even the correct password is refused while locked.
        var locked = await client.PostAsJsonAsync("/api/auth/login", new { userNameOrEmail = Users.Sales4, password = CrmWebFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("locked", (await locked.Content.ReadAsStringAsync()).ToLowerInvariant());

        Assert.True(factory.Db(db => db.AuditLogs.Any(a => a.Action == AuditAction.Lockout && a.UserName == Users.Sales4)));
        Assert.True(factory.Db(db => db.AuditLogs.Count(a => a.Action == AuditAction.LoginFailed && a.UserName == Users.Sales4)) >= 4);
        Assert.True(factory.Db(db => db.Users.Single(u => u.Email == Users.Sales4).LockoutEnd) > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Passwords_are_stored_only_as_identity_hashes()
    {
        var users = factory.Db(db => db.Users.Select(u => u.PasswordHash).ToList());
        Assert.NotEmpty(users);
        Assert.All(users, h =>
        {
            Assert.NotNull(h);
            Assert.DoesNotContain(CrmWebFactory.Password, h);
            Assert.StartsWith("AQAAAA", h); // ASP.NET Core Identity v3 (PBKDF2) format marker
        });
    }

    [Fact]
    public async Task Registration_enforces_password_policy_and_assigns_least_privilege_role()
    {
        var client = factory.Browser();
        var token = await CrmWebFactory.AntiforgeryTokenAsync(client, "/Account/Register");
        var weak = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "Weak Pass", ["Email"] = "weak@example.in", ["Password"] = "password", ["ConfirmPassword"] = "password",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode); // form re-rendered with policy errors
        Assert.False(factory.Db(db => db.Users.Any(u => u.Email == "weak@example.in")));

        var ok = await client.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["FullName"] = "New Seller", ["Email"] = "new.seller@example.in", ["Password"] = "Str0ng#Pass", ["ConfirmPassword"] = "Str0ng#Pass",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var roles = factory.Db(db => (from u in db.Users join ur in db.UserRoles on u.Id equals ur.UserId join r in db.Roles on ur.RoleId equals r.Id
                                      where u.Email == "new.seller@example.in" select r.Name).ToList());
        Assert.Equal(["SalesExecutive"], roles);
    }

    [Fact]
    public async Task Mvc_post_without_antiforgery_token_is_rejected()
    {
        var client = await factory.SignedInBrowserAsync(Users.Admin);
        var response = await client.PostAsync("/Customers/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CustomerName"] = "Csrf Victim", ["Email"] = "csrf@example.in", ["Phone"] = "9000000001", ["Status"] = "Active"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(factory.Db(db => db.Customers.Any(c => c.Email == "csrf@example.in")));
    }

    [Theory]
    [InlineData(Users.Sales, "/Users", false)]
    [InlineData(Users.Sales, "/Roles", false)]
    [InlineData(Users.Sales, "/AuditLogs", false)]
    [InlineData(Users.Sales, "/Reports/UserActivity", false)]
    [InlineData(Users.Manager, "/Roles", false)]
    [InlineData(Users.Manager, "/Users/Create", false)]
    [InlineData(Users.Manager, "/Reports/Audit", false)]
    [InlineData(Users.Manager, "/Reports/Pipeline", true)]
    [InlineData(Users.Manager, "/Users", true)]
    [InlineData(Users.Admin, "/Users/Create", true)]
    [InlineData(Users.Admin, "/Roles", true)]
    [InlineData(Users.Admin, "/AuditLogs", true)]
    [InlineData(Users.Admin, "/Reports/Audit", true)]
    public async Task Role_based_page_access_differs_by_role(string user, string path, bool allowed)
    {
        var client = await factory.SignedInBrowserAsync(user);
        var response = await client.GetAsync(path);
        if (allowed) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
        }
    }

    [Fact]
    public async Task Sales_executive_cannot_open_another_users_record_by_changing_the_url()
    {
        var foreignId = factory.Db(db => db.Customers.Where(c => c.AssignedTo!.Email != Users.Sales).Select(c => c.CustomerId).First());
        var client = await factory.SignedInBrowserAsync(Users.Sales);
        var response = await client.GetAsync($"/Customers/Edit/{foreignId}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", response.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Api_token_is_revoked_on_logout_and_never_exposes_secrets()
    {
        var api = await factory.ApiAsync(Users.Sales2);
        var me = await api.GetStringAsync("/api/auth/me");
        Assert.DoesNotContain("passwordHash", me, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", me, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent, (await api.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Deactivated_users_lose_api_access_immediately()
    {
        var api = await factory.ApiAsync(Users.Sales3);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/customers")).StatusCode);

        var admin = await factory.SignedInBrowserAsync(Users.Admin);
        var id = factory.Db(db => db.Users.Single(u => u.Email == Users.Sales3).Id);
        var token = await CrmWebFactory.AntiforgeryTokenAsync(admin, $"/Users/Details/{id}");
        var r = await admin.PostAsync($"/Users/SetActive/{id}?active=false", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/customers")).StatusCode);
        Assert.True(factory.Db(db => db.AuditLogs.Any(a => a.Action == AuditAction.Deactivate && a.RecordId == id)));

        // restore for other tests
        token = await CrmWebFactory.AntiforgeryTokenAsync(admin, $"/Users/Details/{id}");
        await admin.PostAsync($"/Users/SetActive/{id}?active=true", new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    [Fact]
    public void Audit_log_is_append_only_in_the_application_and_the_database()
    {
        Assert.Throws<InvalidOperationException>(() => factory.Db(db =>
        {
            var entry = db.AuditLogs.First();
            entry.Details = "tampered";
            return db.SaveChanges();
        }));
        var ex = Assert.ThrowsAny<Exception>(() => factory.Db(db => db.Database.ExecuteSqlRaw("UPDATE AuditLogs SET Details = 'x'")));
        Assert.Contains("append-only", ex.Message);
        Assert.ThrowsAny<Exception>(() => factory.Db(db => db.Database.ExecuteSqlRaw("DELETE FROM AuditLogs")));
    }

    [Fact]
    public async Task Security_headers_are_sent()
    {
        var response = await factory.Browser().GetAsync("/Account/Login");
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("script-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
