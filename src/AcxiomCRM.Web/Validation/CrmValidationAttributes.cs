using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using AcxiomCRM.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.Extensions.Options;

namespace AcxiomCRM.Web.Validation;

public static class ValidationMessages
{
    public const string Required = "{0} is required.";
    public const string MaxLength = "{0} cannot exceed {1} characters.";
    public const string LengthRange = "{0} must be between {2} and {1} characters.";
    public const string Email = "Enter a valid email address.";
    public const string Phone = "Enter a valid phone number.";
    public const string AmountPositive = "Opportunity Amount must be greater than 0.";
    public const string AmountNotNegative = "Opportunity Amount cannot be negative.";
    public const string Probability = "Probability must be between 0 and 100.";
    public const string CloseDatePast = "Expected Close Date cannot be in the past.";
    public const string FollowUpDatePast = "Follow-up date cannot be earlier than today.";
    public const string InvalidOption = "Select a valid {0}.";
}

internal static class ClientAttr
{
    public static void Merge(IDictionary<string, string> attributes, string key, string value)
    {
        if (!attributes.ContainsKey(key)) attributes.Add(key, value);
    }
}

/// <summary>Email format rule shared by client (regex adapter) and server.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class CrmEmailAttribute : ValidationAttribute, IClientModelValidator
{
    private static readonly Regex Pattern = new(ValidationDefaults.EmailPattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

    public CrmEmailAttribute() : base(ValidationMessages.Email) { }

    public override bool IsValid(object? value) =>
        value is null || value is string { Length: 0 } || value is string s && s.Length <= 254 && Pattern.IsMatch(s.Trim());

    public void AddValidation(ClientModelValidationContext context)
    {
        ClientAttr.Merge(context.Attributes, "data-val", "true");
        ClientAttr.Merge(context.Attributes, "data-val-regex", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        ClientAttr.Merge(context.Attributes, "data-val-regex-pattern", ValidationDefaults.EmailPattern);
    }
}

/// <summary>Phone rule driven by the configurable Validation:PhonePattern (default: 10-digit Indian mobile).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class CrmPhoneAttribute : ValidationAttribute, IClientModelValidator
{
    public CrmPhoneAttribute() : base(ValidationMessages.Phone) { }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is string { Length: 0 }) return ValidationResult.Success;
        var pattern = PatternFrom(validationContext.GetService(typeof(IOptions<CrmValidationOptions>)) as IOptions<CrmValidationOptions>);
        var ok = value is string s && Regex.IsMatch(s.Trim(), pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
        return ok ? ValidationResult.Success : new ValidationResult(FormatErrorMessage(validationContext.DisplayName), MemberNames(validationContext));
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        var options = context.ActionContext.HttpContext.RequestServices.GetService(typeof(IOptions<CrmValidationOptions>)) as IOptions<CrmValidationOptions>;
        ClientAttr.Merge(context.Attributes, "data-val", "true");
        ClientAttr.Merge(context.Attributes, "data-val-regex", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        ClientAttr.Merge(context.Attributes, "data-val-regex-pattern", PatternFrom(options));
    }

    private static string PatternFrom(IOptions<CrmValidationOptions>? options) =>
        string.IsNullOrWhiteSpace(options?.Value.PhonePattern) ? ValidationDefaults.PhonePattern : options!.Value.PhonePattern;

    internal static string[]? MemberNames(ValidationContext ctx) => ctx.MemberName is null ? null : [ctx.MemberName];
}

/// <summary>
/// Date must be today or later (business time zone). Optionally only enforced while a sibling
/// property (e.g. Stage) is not one of <see cref="InactiveValues"/> — "active" records only.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotBeforeTodayAttribute : ValidationAttribute, IClientModelValidator
{
    public NotBeforeTodayAttribute(string errorMessage) : base(errorMessage) { }

    public string? DependentProperty { get; init; }
    public string[] InactiveValues { get; init; } = [];

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not DateTime date) return ValidationResult.Success;
        if (!AppliesTo(validationContext.ObjectInstance)) return ValidationResult.Success;

        var clock = validationContext.GetService(typeof(IBusinessClock)) as IBusinessClock;
        var today = clock?.Today ?? DateTime.Today;
        return date.Date >= today
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName), CrmPhoneAttribute.MemberNames(validationContext));
    }

    private bool AppliesTo(object instance)
    {
        if (DependentProperty is null) return true;
        var dependent = instance.GetType().GetProperty(DependentProperty)?.GetValue(instance)?.ToString();
        return dependent is null || !InactiveValues.Contains(dependent, StringComparer.OrdinalIgnoreCase);
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ClientAttr.Merge(context.Attributes, "data-val", "true");
        ClientAttr.Merge(context.Attributes, "data-val-notbeforetoday", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        ClientAttr.Merge(context.Attributes, "data-val-notbeforetoday-dependent", DependentProperty ?? string.Empty);
        ClientAttr.Merge(context.Attributes, "data-val-notbeforetoday-inactive", string.Join(",", InactiveValues));
    }
}

/// <summary>Numeric value must be &gt; 0 while a sibling property is not one of the inactive values.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PositiveWhenActiveAttribute : ValidationAttribute, IClientModelValidator
{
    public PositiveWhenActiveAttribute(string dependentProperty, params string[] inactiveValues) : base(ValidationMessages.AmountPositive)
    {
        DependentProperty = dependentProperty;
        InactiveValues = inactiveValues;
    }

    public string DependentProperty { get; }
    public string[] InactiveValues { get; }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null) return ValidationResult.Success;
        var dependent = validationContext.ObjectInstance.GetType().GetProperty(DependentProperty)?.GetValue(validationContext.ObjectInstance)?.ToString();
        if (dependent is not null && InactiveValues.Contains(dependent, StringComparer.OrdinalIgnoreCase)) return ValidationResult.Success;

        var number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        return number > 0
            ? ValidationResult.Success
            : new ValidationResult(FormatErrorMessage(validationContext.DisplayName), CrmPhoneAttribute.MemberNames(validationContext));
    }

    public void AddValidation(ClientModelValidationContext context)
    {
        ClientAttr.Merge(context.Attributes, "data-val", "true");
        ClientAttr.Merge(context.Attributes, "data-val-positivewhenactive", FormatErrorMessage(context.ModelMetadata.GetDisplayName()));
        ClientAttr.Merge(context.Attributes, "data-val-positivewhenactive-dependent", DependentProperty);
        ClientAttr.Merge(context.Attributes, "data-val-positivewhenactive-inactive", string.Join(",", InactiveValues));
    }
}

/// <summary>Rejects enum values that are not defined (e.g. tampered numeric form values).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ValidEnumAttribute : ValidationAttribute
{
    public ValidEnumAttribute() : base(ValidationMessages.InvalidOption) { }

    public override bool IsValid(object? value) =>
        value is null || (value.GetType().IsEnum && Enum.IsDefined(value.GetType(), value));

    public override string FormatErrorMessage(string name) => string.Format(CultureInfo.CurrentCulture, ErrorMessageString, name.ToLowerInvariant());
}
