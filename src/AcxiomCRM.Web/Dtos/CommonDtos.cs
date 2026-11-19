using AcxiomCRM.Web.Models;

namespace AcxiomCRM.Web.Dtos;

public sealed class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

/// <summary>Common search / sort / paging parameters for list endpoints and pages.</summary>
public class ListQuery
{
    public const int MaxPageSize = 100;

    /// <summary>Free-text search.</summary>
    public string? Search { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    /// <summary>Sort column key (see each endpoint). Unknown keys fall back to the default order.</summary>
    public string? Sort { get; set; }

    /// <summary>"asc" or "desc".</summary>
    public string? Dir { get; set; }

    public bool Descending => string.Equals(Dir, "desc", StringComparison.OrdinalIgnoreCase);

    public void Normalize()
    {
        if (Page < 1) Page = 1;
        if (PageSize < 1) PageSize = 10;
        if (PageSize > MaxPageSize) PageSize = MaxPageSize;
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        if (Search is { Length: > 100 }) Search = Search[..100];
    }
}

/// <summary>A selectable record for dropdowns.</summary>
public sealed record OptionDto(string Value, string Text, string? Group = null);

public sealed record UserSummaryDto(string Id, string FullName, string Email, string? Role);

public sealed record RelatedRecordDto(RelatedRecordType Type, int Id, string Name);
