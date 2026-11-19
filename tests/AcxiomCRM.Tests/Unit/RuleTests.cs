using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Web.Domain;
using AcxiomCRM.Web.Dtos;
using AcxiomCRM.Web.Models;
using AcxiomCRM.Web.Services;
using AcxiomCRM.Web.Validation;

namespace AcxiomCRM.Tests.Unit;

public class OpportunityRuleTests
{
    private static readonly DateTime Today = new(2026, 11, 19);

    [Fact]
    public void Weighted_pipeline_is_amount_times_probability_over_100() =>
        Assert.Equal(25000m, OpportunityRules.Weighted(100000m, 25));

    [Theory]
    [InlineData(OpportunityStage.Proposal, 0, false)]
    [InlineData(OpportunityStage.Proposal, 1, true)]
    [InlineData(OpportunityStage.Lost, 0, true)]
    public void Amount_must_be_positive_for_active_opportunities(OpportunityStage stage, decimal amount, bool valid)
    {
        var v = OpportunityRules.Validate(stage, amount, stage == OpportunityStage.Lost ? 0 : 50, Today, Today, null, "reason");
        Assert.Equal(valid, !v.Errors.ContainsKey("Amount"));
    }

    [Fact]
    public void Close_date_in_the_past_only_matters_while_active()
    {
        Assert.True(OpportunityRules.Validate(OpportunityStage.Negotiation, 10, 50, Today.AddDays(-1), Today, null, null).Errors.ContainsKey("ExpectedCloseDate"));
        Assert.False(OpportunityRules.Validate(OpportunityStage.Won, 10, 100, Today.AddDays(-1), Today, null, null).Errors.ContainsKey("ExpectedCloseDate"));
        Assert.False(OpportunityRules.Validate(OpportunityStage.Negotiation, 10, 50, Today, Today, null, null).Any);
    }

    [Fact]
    public void Closed_opportunities_cannot_be_reopened() =>
        Assert.True(OpportunityRules.Validate(OpportunityStage.Proposal, 10, 50, Today, Today, OpportunityStage.Won, null).Errors.ContainsKey("Stage"));
}

public class LeadWorkflowTests
{
    [Theory]
    [InlineData(LeadStatus.New, LeadStatus.Contacted, true)]
    [InlineData(LeadStatus.New, LeadStatus.Qualified, false)]
    [InlineData(LeadStatus.Contacted, LeadStatus.Qualified, true)]
    [InlineData(LeadStatus.Qualified, LeadStatus.Converted, false)] // only via conversion
    [InlineData(LeadStatus.Lost, LeadStatus.New, false)]
    [InlineData(LeadStatus.Converted, LeadStatus.Contacted, false)]
    [InlineData(LeadStatus.Unqualified, LeadStatus.Contacted, true)]
    public void Transitions_follow_the_configured_workflow(LeadStatus from, LeadStatus to, bool allowed) =>
        Assert.Equal(allowed, LeadWorkflow.CanTransition(from, to));

    [Fact]
    public void Only_qualified_leads_can_convert()
    {
        Assert.True(LeadWorkflow.CanConvert(LeadStatus.Qualified));
        Assert.All(Enum.GetValues<LeadStatus>().Where(s => s != LeadStatus.Qualified), s => Assert.False(LeadWorkflow.CanConvert(s)));
    }
}

public class FollowUpRuleTests
{
    [Fact]
    public void New_follow_up_date_cannot_be_before_today()
    {
        var today = new DateTime(2026, 11, 19);
        Assert.True(FollowUpRules.ValidateDate(today.AddDays(-1).AddHours(23), today).Any);
        Assert.False(FollowUpRules.ValidateDate(today.AddHours(9), today).Any);
    }

    [Fact]
    public void Overdue_means_planned_and_in_the_past()
    {
        var now = new DateTime(2026, 11, 19, 12, 0, 0);
        Assert.True(FollowUpRules.IsOverdue(FollowUpStatus.Planned, now.AddHours(-1), now));
        Assert.False(FollowUpRules.IsOverdue(FollowUpStatus.Completed, now.AddHours(-1), now));
        Assert.False(FollowUpRules.IsOverdue(FollowUpStatus.Planned, now.AddHours(1), now));
    }
}

public class ValidationAttributeTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("first.last+tag@company.co.in", true)]
    [InlineData("user@", false)]
    [InlineData("user example.com", false)]
    [InlineData("user@localhost", false)]
    public void Email_format(string email, bool valid) => Assert.Equal(valid, new CrmEmailAttribute().IsValid(email));

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("6123456789", true)]
    [InlineData("5123456789", false)]
    [InlineData("98765", false)]
    [InlineData("98765432101", false)]
    [InlineData("98765abcde", false)]
    public void Phone_follows_the_configured_indian_mobile_rule(string phone, bool valid)
    {
        var model = new CustomerSaveRequest { CustomerName = "Valid Name", Email = "a@b.in", Phone = phone, Status = CustomerStatus.Active };
        Assert.Equal(valid, Validate(model).All(r => !r.MemberNames.Contains(nameof(CustomerSaveRequest.Phone))));
    }

    [Fact]
    public void Customer_required_fields_and_messages()
    {
        var results = Validate(new CustomerSaveRequest { Status = CustomerStatus.Active });
        Assert.Contains(results, r => r.ErrorMessage == "Customer Name is required.");
        Assert.Contains(results, r => r.ErrorMessage == "Email is required.");
        Assert.Contains(results, r => r.ErrorMessage == "Phone is required.");
    }

    [Fact]
    public void Opportunity_attributes_apply_the_active_stage_rules()
    {
        var active = new OpportunitySaveRequest { OpportunityName = "Deal", CustomerId = 1, Amount = 0, Probability = 101, Stage = OpportunityStage.Proposal, ExpectedCloseDate = DateTime.Today.AddDays(-1) };
        var errors = Validate(active).Select(r => r.ErrorMessage).ToList();
        Assert.Contains(ValidationMessages.AmountPositive, errors);
        Assert.Contains(ValidationMessages.Probability, errors);
        Assert.Contains(ValidationMessages.CloseDatePast, errors);

        var lost = new OpportunitySaveRequest { OpportunityName = "Deal", CustomerId = 1, Amount = 0, Probability = 0, Stage = OpportunityStage.Lost, ExpectedCloseDate = DateTime.Today.AddDays(-1) };
        Assert.Empty(Validate(lost));
    }

    [Fact]
    public void Undefined_enum_values_are_rejected()
    {
        var model = new LeadSaveRequest { LeadName = "Lead", Email = "a@b.in", Source = LeadSource.Website, Priority = LeadPriority.Low, Status = (LeadStatus)99 };
        Assert.Contains(Validate(model), r => r.MemberNames.Contains(nameof(LeadSaveRequest.Status)));
    }
}

public class CsvExportTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1+cmd|' /C calc'!A0", "'+1+cmd|' /C calc'!A0")]
    [InlineData("@SUM(A1:A2)", "'@SUM(A1:A2)")]
    [InlineData("-5", "-5")]          // genuine numbers are left intact
    [InlineData("+911234", "+911234")]
    [InlineData("Plain, with comma", "\"Plain, with comma\"")]
    public void Cells_are_escaped_against_formula_injection(string input, string expected) =>
        Assert.Equal(expected, ReportService.Escape(input));
}
