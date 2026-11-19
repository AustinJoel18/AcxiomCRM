using AcxiomCRM.Web.Domain;

namespace AcxiomCRM.Web.Services;

public enum ResultStatus
{
    Success,
    NotFound,
    Forbidden,
    Invalid,
    Conflict
}

public class ServiceResult
{
    public ResultStatus Status { get; protected init; }
    public Dictionary<string, List<string>> Errors { get; protected init; } = new();
    public bool Succeeded => Status == ResultStatus.Success;

    /// <summary>First error message (useful for flash messages and 409 titles).</summary>
    public string? Message => Errors.Values.SelectMany(v => v).FirstOrDefault();

    public static ServiceResult Ok() => new() { Status = ResultStatus.Success };
    public static ServiceResult NotFound() => new() { Status = ResultStatus.NotFound };
    public static ServiceResult Forbidden(string? message = null) => new() { Status = ResultStatus.Forbidden, Errors = Single("", message) };
    public static ServiceResult Invalid(Dictionary<string, List<string>> errors) => new() { Status = ResultStatus.Invalid, Errors = errors };
    public static ServiceResult Invalid(string field, string message) => Invalid(Single(field, message));
    public static ServiceResult Conflict(string field, string message) => new() { Status = ResultStatus.Conflict, Errors = Single(field, message) };

    protected static Dictionary<string, List<string>> Single(string field, string? message) =>
        message is null ? new() : new() { [field] = [message] };
}

public sealed class ServiceResult<T> : ServiceResult
{
    public T? Value { get; private init; }

    public static ServiceResult<T> Ok(T value) => new() { Status = ResultStatus.Success, Value = value };
    public new static ServiceResult<T> NotFound() => new() { Status = ResultStatus.NotFound };
    public new static ServiceResult<T> Forbidden(string? message = null) => new() { Status = ResultStatus.Forbidden, Errors = Single("", message) };
    public new static ServiceResult<T> Invalid(Dictionary<string, List<string>> errors) => new() { Status = ResultStatus.Invalid, Errors = errors };
    public new static ServiceResult<T> Invalid(string field, string message) => Invalid(Single(field, message));
    public static ServiceResult<T> Invalid(RuleViolations v) => Invalid(v.Errors);
    public new static ServiceResult<T> Conflict(string field, string message) => new() { Status = ResultStatus.Conflict, Errors = Single(field, message) };

    /// <summary>Re-type a failed result.</summary>
    public static ServiceResult<T> From(ServiceResult failed) => new() { Status = failed.Status, Errors = failed.Errors };
}

internal static class ErrorDictionaryExtensions
{
    public static Dictionary<string, List<string>> Merge(this Dictionary<string, List<string>> target, Dictionary<string, List<string>> source)
    {
        foreach (var (key, messages) in source)
        {
            if (!target.TryGetValue(key, out var list)) target[key] = list = [];
            foreach (var m in messages.Where(m => !list.Contains(m))) list.Add(m);
        }
        return target;
    }

    public static void Add(this Dictionary<string, List<string>> target, string key, string message)
    {
        if (!target.TryGetValue(key, out var list)) target[key] = list = [];
        if (!list.Contains(message)) list.Add(message);
    }
}
