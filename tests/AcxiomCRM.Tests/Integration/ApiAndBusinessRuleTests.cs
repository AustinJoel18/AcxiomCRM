using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AcxiomCRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Tests.Integration;

/// <summary>REST API status codes, data scoping, server-side validation and CRM business rules (§10, §13, §17.19).</summary>
public sealed class ApiAndBusinessRuleTests(CrmWebFactory factory) : IClassFixture<CrmWebFactory>
{
    private static readonly string Today = DateTime.UtcNow.AddHours(5.5).ToString("yyyy-MM-dd"); // business time zone (IST)
    private static readonly string Yesterday = DateTime.UtcNow.AddHours(5.5).AddDays(-1).ToString("yyyy-MM-dd");
    private static readonly string NextMonth = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(CrmWebFactory.Json);

    private static string[] Errors(JsonElement problem, string field) =>
        problem.GetProperty("errors").EnumerateObject().Where(p => p.Name.Equals(field, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Value.EnumerateArray().Select(v => v.GetString()!)).ToArray();

    private int OwnCustomerId(string email) => factory.Db(db => db.Customers.Where(c => c.AssignedTo!.Email == email).Select(c => c.CustomerId).First());

    [Fact]
    public async Task Get_customers_returns_authorized_json()
    {
        var api = await factory.ApiAsync(Users.Admin);
        var response = await api.GetAsync("/api/customers?pageSize=5&sort=name");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJson(response);
        Assert.Equal(5, body.GetProperty("items").GetArrayLength());
        Assert.True(body.GetProperty("totalCount").GetInt32() >= 28);
    }

    [Fact]
    public async Task Data_is_scoped_by_role_admin_all_manager_team_sales_own()
    {
        async Task<JsonElement> List(string user) => await ReadJson(await (await factory.ApiAsync(user)).GetAsync("/api/customers?pageSize=100"));
        var all = (await List(Users.Admin)).GetProperty("totalCount").GetInt32();
        var team = await List(Users.Manager);
        var own = await List(Users.Sales);

        var teamOwners = team.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("assignedToName").GetString()).Distinct().ToHashSet();
        Assert.Subset(new HashSet<string?> { "Rahul Verma", "Vikram Singh", "Sneha Patel" }, teamOwners);
        Assert.All(own.GetProperty("items").EnumerateArray(), i => Assert.Equal("Vikram Singh", i.GetProperty("assignedToName").GetString()));
        Assert.True(all > team.GetProperty("totalCount").GetInt32());
        Assert.True(team.GetProperty("totalCount").GetInt32() > own.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Out_of_scope_records_are_forbidden_and_missing_records_are_not_found()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var foreign = OwnCustomerId(Users.Sales3);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync($"/api/customers/{foreign}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.PutAsJsonAsync($"/api/customers/{foreign}",
            new { customerName = "Hijack", email = "hijack@example.in", phone = "9000000002", status = "Active" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/api/customers/999999")).StatusCode);
    }

    [Fact]
    public async Task Sales_executive_cannot_delete_or_assign_outside_scope()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var own = OwnCustomerId(Users.Sales);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.DeleteAsync($"/api/customers/{own}")).StatusCode);

        var otherUser = factory.Db(db => db.Users.Single(u => u.Email == Users.Sales3).Id);
        var r = await api.PostAsJsonAsync("/api/customers", new
        {
            customerName = "Tamper Assign", email = "tamper.assign@example.in", phone = "9000000003", status = "Active", assignedToId = otherUser
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.NotEmpty(Errors(await ReadJson(r), "AssignedToId"));
    }

    [Fact]
    public async Task Customer_validation_is_enforced_on_the_server()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var r = await api.PostAsJsonAsync("/api/customers", new { customerName = "", email = "not-an-email", phone = "12345", status = "Active" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var p = await ReadJson(r);
        Assert.Contains("Enter a valid email address.", Errors(p, "Email"));
        Assert.Contains("Enter a valid phone number.", Errors(p, "Phone"));
        Assert.Contains("Customer Name is required.", Errors(p, "CustomerName"));

        var tooLong = await api.PostAsJsonAsync("/api/customers", new { customerName = new string('x', 101), email = "long@example.in", phone = "9000000004", status = "Active" });
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);

        var badEnum = await api.PostAsJsonAsync("/api/customers", new { customerName = "Enum Test", email = "enum@example.in", phone = "9000000005", status = "Deleted" });
        Assert.Equal(HttpStatusCode.BadRequest, badEnum.StatusCode);
    }

    [Fact]
    public async Task Duplicate_customer_email_phone_or_name_returns_conflict()
    {
        var api = await factory.ApiAsync(Users.Admin);
        var existing = factory.Db(db => db.Customers.AsNoTracking().First());

        var dupEmail = await api.PostAsJsonAsync("/api/customers", new { customerName = "Someone New", email = existing.Email.ToUpperInvariant(), phone = "9000000006", status = "Active" });
        Assert.Equal(HttpStatusCode.Conflict, dupEmail.StatusCode);

        var dupPhone = await api.PostAsJsonAsync("/api/customers", new { customerName = "Someone Else", email = "someone.else@example.in", phone = existing.Phone, status = "Active" });
        Assert.Equal(HttpStatusCode.Conflict, dupPhone.StatusCode);

        var dupName = await api.PostAsJsonAsync("/api/customers", new { customerName = existing.CustomerName, companyName = existing.CompanyName, email = "dup.name@example.in", phone = "9000000007", status = "Active" });
        Assert.Equal(HttpStatusCode.Conflict, dupName.StatusCode);
    }

    [Fact]
    public async Task Customer_crud_creates_audit_entries_with_old_and_new_values()
    {
        var api = await factory.ApiAsync(Users.Manager);
        var created = await api.PostAsJsonAsync("/api/customers", new { customerName = "Audit Trail Co", email = "audit.trail@example.in", phone = "9000000008", status = "Prospect" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        var id = (await ReadJson(created)).GetProperty("customerId").GetInt32();

        var updated = await api.PutAsJsonAsync($"/api/customers/{id}", new { customerName = "Audit Trail Co", email = "audit.trail@example.in", phone = "9000000008", status = "Active" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/customers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/customers/{id}")).StatusCode);

        var logs = factory.Db(db => db.AuditLogs.AsNoTracking().Where(a => a.EntityName == "Customer" && a.RecordId == id.ToString()).OrderBy(a => a.AuditLogId).ToList());
        Assert.Equal([AuditAction.Create, AuditAction.StatusChange, AuditAction.Delete], logs.Select(l => l.Action));
        Assert.All(logs, l => Assert.Equal("manager@acxiomcrm.local", l.UserName));
        Assert.Contains("\"Status\":\"Prospect\"", logs[1].OldValue);
        Assert.Contains("\"Status\":\"Active\"", logs[1].NewValue);

        // soft delete: row is retained for history
        Assert.True(factory.Db(db => db.Customers.IgnoreQueryFilters().Single(c => c.CustomerId == id).IsDeleted));
    }

    [Theory]
    [InlineData(0, 50, 30, "Amount", "Opportunity Amount must be greater than 0.")]
    [InlineData(-10, 50, 30, "Amount", "Opportunity Amount cannot be negative.")]
    [InlineData(10000, 101, 30, "Probability", "Probability must be between 0 and 100.")]
    [InlineData(10000, -1, 30, "Probability", "Probability must be between 0 and 100.")]
    [InlineData(10000, 50, -1, "ExpectedCloseDate", "Expected Close Date cannot be in the past.")]
    public async Task Opportunity_business_rules_are_enforced(decimal amount, int probability, int closeInDays, string field, string message)
    {
        var api = await factory.ApiAsync(Users.Sales);
        var close = DateTime.UtcNow.AddHours(5.5).AddDays(closeInDays).ToString("yyyy-MM-dd");
        var r = await api.PostAsJsonAsync("/api/opportunities", new
        {
            opportunityName = "Rule check", customerId = OwnCustomerId(Users.Sales), amount, probability, stage = "Proposal", expectedCloseDate = close
        });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains(message, Errors(await ReadJson(r), field));
    }

    [Fact]
    public async Task Valid_opportunity_is_created_with_weighted_amount_and_closed_deals_cannot_reopen()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var r = await api.PostAsJsonAsync("/api/opportunities", new
        {
            opportunityName = "Weighted deal", customerId = OwnCustomerId(Users.Sales), amount = 200000, probability = 25, stage = "Qualification", expectedCloseDate = Today
        });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var opp = await ReadJson(r);
        Assert.Equal(50000m, opp.GetProperty("weightedAmount").GetDecimal());
        var id = opp.GetProperty("opportunityId").GetInt32();

        var won = await ReadJson(await api.PostAsJsonAsync($"/api/opportunities/{id}/stage", new { stage = "Won", closeReason = "Signed" }));
        Assert.Equal("Won", won.GetProperty("status").GetString());
        Assert.Equal(100, won.GetProperty("probability").GetInt32());

        var reopen = await api.PostAsJsonAsync($"/api/opportunities/{id}/stage", new { stage = "Proposal" });
        Assert.Equal(HttpStatusCode.BadRequest, reopen.StatusCode);
    }

    [Fact]
    public async Task Lost_opportunity_requires_a_reason()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var id = (await ReadJson(await api.PostAsJsonAsync("/api/opportunities", new
        {
            opportunityName = "Will be lost", customerId = OwnCustomerId(Users.Sales), amount = 5000, probability = 20, stage = "Qualification", expectedCloseDate = NextMonth
        }))).GetProperty("opportunityId").GetInt32();
        var r = await api.PostAsJsonAsync($"/api/opportunities/{id}/stage", new { stage = "Lost" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.NotEmpty(Errors(await ReadJson(r), "CloseReason"));
    }

    [Fact]
    public async Task Follow_up_cannot_be_dated_before_today_but_today_is_allowed()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var customer = OwnCustomerId(Users.Sales);
        var past = await api.PostAsJsonAsync("/api/followups", new { subject = "Call back", followUpType = "Call", followUpDate = Yesterday + "T10:00", relatedType = "Customer", relatedId = customer });
        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);
        Assert.Contains("Follow-up date cannot be earlier than today.", Errors(await ReadJson(past), "FollowUpDate"));

        var ok = await api.PostAsJsonAsync("/api/followups", new { subject = "Call back", followUpType = "Call", followUpDate = Today + "T23:00", relatedType = "Customer", relatedId = customer });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var id = (await ReadJson(ok)).GetProperty("followUpId").GetInt32();

        var reschedulePast = await api.PostAsJsonAsync($"/api/followups/{id}/reschedule", new { newDate = Yesterday + "T09:00" });
        Assert.Equal(HttpStatusCode.BadRequest, reschedulePast.StatusCode);

        var done = await api.PostAsJsonAsync($"/api/followups/{id}/complete", new { outcome = "Spoke to the customer." });
        Assert.Equal("Completed", (await ReadJson(done)).GetProperty("status").GetString());
        Assert.True(factory.Db(db => db.Activities.Any(a => a.FollowUpId == id)));          // related record updated
        Assert.True(factory.Db(db => db.AuditLogs.Any(a => a.EntityName == "FollowUp" && a.RecordId == id.ToString() && a.Action == AuditAction.Complete)));
    }

    [Fact]
    public async Task Follow_up_for_out_of_scope_record_is_rejected()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var r = await api.PostAsJsonAsync("/api/followups", new { subject = "Sneaky", followUpType = "Call", followUpDate = NextMonth + "T10:00", relatedType = "Customer", relatedId = OwnCustomerId(Users.Sales3) });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Lead_status_workflow_and_conversion()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var created = await api.PostAsJsonAsync("/api/leads", new
        {
            leadName = "Workflow Lead", companyName = "Workflow Pvt Ltd", email = "workflow.lead@example.in", phone = "9811122233",
            source = "Website", status = "New", priority = "High", expectedValue = 150000
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await ReadJson(created)).GetProperty("leadId").GetInt32();

        // New -> Qualified skips a step; Converted is only reachable through conversion.
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/api/leads/{id}/status", new { status = "Qualified" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/api/leads/{id}/status", new { status = "Converted" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/api/leads/{id}/convert", new { customer = new { } })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync($"/api/leads/{id}/status", new { status = "Contacted" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync($"/api/leads/{id}/status", new { status = "Qualified" })).StatusCode);

        var convert = await api.PostAsJsonAsync($"/api/leads/{id}/convert", new
        {
            customer = new { customerName = "Workflow Lead", companyName = "Workflow Pvt Ltd", email = "workflow.lead@example.in", phone = "9811122233", status = "Active" },
            opportunity = new { opportunityName = "Workflow first deal", amount = 150000, probability = 30, expectedCloseDate = NextMonth }
        });
        Assert.Equal(HttpStatusCode.OK, convert.StatusCode);
        var result = await ReadJson(convert);
        var customerId = result.GetProperty("customerId").GetInt32();

        var lead = await ReadJson(await api.GetAsync($"/api/leads/{id}"));
        Assert.Equal("Converted", lead.GetProperty("status").GetString());
        Assert.Equal(customerId, lead.GetProperty("convertedCustomerId").GetInt32());
        Assert.True(factory.Db(db => db.Opportunities.Any(o => o.LeadId == id && o.CustomerId == customerId)));
        Assert.True(factory.Db(db => db.AuditLogs.Any(a => a.EntityName == "Lead" && a.RecordId == id.ToString() && a.Action == AuditAction.Convert)));

        // A converted lead cannot be converted or edited again.
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync($"/api/leads/{id}/convert", new { existingCustomerId = customerId })).StatusCode);
    }

    [Fact]
    public async Task Lead_requires_valid_status_and_contact()
    {
        var api = await factory.ApiAsync(Users.Sales);
        var noContact = await api.PostAsJsonAsync("/api/leads", new { leadName = "No Contact", source = "Website", status = "New", priority = "Low" });
        Assert.Equal(HttpStatusCode.BadRequest, noContact.StatusCode);
        var tooValuable = await api.PostAsJsonAsync("/api/leads", new { leadName = "Big", email = "big@example.in", source = "Website", status = "New", priority = "Low", expectedValue = 1e12 });
        Assert.Equal(HttpStatusCode.BadRequest, tooValuable.StatusCode);
        var numericStatus = await api.PostAsJsonAsync("/api/leads", new { leadName = "Numeric", email = "num@example.in", source = "Website", status = 99, priority = "Low" });
        Assert.Equal(HttpStatusCode.BadRequest, numericStatus.StatusCode);
    }

    [Fact]
    public async Task Pipeline_report_api_is_scoped()
    {
        var admin = await ReadJson(await (await factory.ApiAsync(Users.Admin)).GetAsync("/api/reports/pipeline"));
        var sales = await ReadJson(await (await factory.ApiAsync(Users.Sales)).GetAsync("/api/reports/pipeline"));
        Assert.Equal("All records", admin.GetProperty("scope").GetString());
        Assert.Equal("Own/Assigned", sales.GetProperty("scope").GetString());
        Assert.True(admin.GetProperty("totalOpenAmount").GetDecimal() > sales.GetProperty("totalOpenAmount").GetDecimal());
        Assert.All(sales.GetProperty("owners").EnumerateArray(), o => Assert.Equal("Vikram Singh", o.GetProperty("ownerName").GetString()));
    }

    [Fact]
    public async Task Mvc_form_rejects_crafted_invalid_request_and_redisplays_errors()
    {
        var client = await factory.SignedInBrowserAsync(Users.Sales);
        var token = await CrmWebFactory.AntiforgeryTokenAsync(client, "/Customers/Create");
        var response = await client.PostAsync("/Customers/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["CustomerName"] = "Crafted", ["Email"] = "crafted-at-example", ["Phone"] = "abc", ["Status"] = "Active", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Enter a valid email address.", html);
        Assert.Contains("Enter a valid phone number.", html);
        Assert.False(factory.Db(db => db.Customers.Any(c => c.CustomerName == "Crafted")));
    }

    [Fact]
    public async Task Reports_render_and_export_csv()
    {
        var client = await factory.SignedInBrowserAsync(Users.Manager);
        foreach (var path in new[] { "/Reports/Customers", "/Reports/Leads?status=Converted", "/Reports/FollowUps?status=Overdue", "/Reports/Opportunities?sort=amount&dir=desc", "/Reports/Pipeline", "/Reports/Conversion", "/Reports/UserActivity" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);

        var csv = await client.GetAsync("/Reports/Leads?export=csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("Code,Lead,", (await csv.Content.ReadAsStringAsync()).TrimStart('﻿'));
    }

    [Fact]
    public async Task OpenApi_document_describes_the_secured_endpoints()
    {
        var doc = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/customers", doc);
        Assert.Contains("/api/auth/login", doc);
        Assert.Contains("bearer", doc, StringComparison.OrdinalIgnoreCase);
    }
}
