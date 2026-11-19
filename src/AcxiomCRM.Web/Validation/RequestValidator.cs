using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.Web.Validation;

/// <summary>
/// Runs DataAnnotations (incl. IValidatableObject) at the service boundary so that business
/// services never persist an unvalidated request, regardless of which controller called them.
/// </summary>
public interface IRequestValidator
{
    Dictionary<string, List<string>> Validate(object request, string prefix = "");
}

public sealed class RequestValidator(IServiceProvider services) : IRequestValidator
{
    public Dictionary<string, List<string>> Validate(object request, string prefix = "")
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request, services, null), results, validateAllProperties: true);

        var errors = new Dictionary<string, List<string>>();
        foreach (var r in results)
        {
            var members = r.MemberNames.Any() ? r.MemberNames : [string.Empty];
            foreach (var m in members)
            {
                var key = string.IsNullOrEmpty(m) ? prefix.TrimEnd('.') : prefix + m;
                if (!errors.TryGetValue(key, out var list)) errors[key] = list = [];
                if (r.ErrorMessage is not null && !list.Contains(r.ErrorMessage)) list.Add(r.ErrorMessage);
            }
        }
        return errors;
    }
}
