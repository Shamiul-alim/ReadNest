using System.ComponentModel.DataAnnotations;
using ReadNest.Domain.Enums;

namespace ReadNest.Web.Components.Forms;

// Form models give immediate feedback in the browser. They are a convenience only: every rule is enforced
// again by the application services and domain entities, which are the security boundary.

public sealed class EntryDetailsFormModel : IValidatableObject
{
    [Range(1, 100_000, ErrorMessage = "Total pages must be between 1 and 100,000.")]
    public int? TotalPages { get; set; }

    public DateOnly? StartedOn { get; set; }

    public DateOnly? CompletedOn { get; set; }

    [StringLength(4000)]
    public string? PrivateNotes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartedOn is { } start && CompletedOn is { } end && start > end)
        {
            yield return new ValidationResult("The completion date cannot be before the start date.", [nameof(CompletedOn)]);
        }
    }
}

public sealed class OwnershipFormModel
{
    public OwnershipStatus Ownership { get; set; }

    public BookFormat? Format { get; set; }

    public DateOnly? AcquiredOn { get; set; }

    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Price must be between 0 and 1,000,000.")]
    public decimal? PurchasePrice { get; set; }

    [StringLength(500)]
    public string? ConditionNotes { get; set; }
}

public sealed class LoanFormModel : IValidatableObject
{
    [Required(ErrorMessage = "Who borrowed the book?")]
    [StringLength(200)]
    public string BorrowerName { get; set; } = "";

    [Required]
    public DateOnly? LoanedOn { get; set; }

    public DateOnly? DueOn { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (DueOn is { } due && LoanedOn is { } loaned && due < loaned)
        {
            yield return new ValidationResult("The expected return date cannot be before the loan date.", [nameof(DueOn)]);
        }
    }
}

public sealed class ManualBookFormModel
{
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(500)]
    public string Title { get; set; } = "";

    [StringLength(500)]
    public string? Subtitle { get; set; }

    [StringLength(1000)]
    public string? Authors { get; set; }

    [StringLength(20)]
    public string? Isbn { get; set; }

    [Range(1, 100_000)]
    public int? PageCount { get; set; }

    [Range(1000, 2100)]
    public int? PublishedYear { get; set; }

    [StringLength(300)]
    public string? Publisher { get; set; }

    [StringLength(3)]
    public string? LanguageCode { get; set; }

    [StringLength(10_000)]
    public string? Description { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Enter a full https:// address.")]
    public string? CoverUrl { get; set; }

    [StringLength(500)]
    public string? Genres { get; set; }

    public ReadingStatus Status { get; set; } = ReadingStatus.WantToRead;

    public static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}

public sealed class ChallengeFormModel : IValidatableObject
{
    [Required]
    [StringLength(120, MinimumLength = 3)]
    public string Title { get; set; } = "";

    [StringLength(1000)]
    public string? Description { get; set; }

    [Range(1, 500)]
    public int TargetBooks { get; set; } = 5;

    [Required]
    public DateOnly? StartsOn { get; set; }

    [Required]
    public DateOnly? EndsOn { get; set; }

    public bool IsPublic { get; set; } = true;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartsOn is { } start && EndsOn is { } end && end < start)
        {
            yield return new ValidationResult("The challenge must end on or after its start date.", [nameof(EndsOn)]);
        }
    }
}

public sealed class ProfileFormModel
{
    [Required]
    [StringLength(80)]
    public string DisplayName { get; set; } = "";

    [Required]
    [StringLength(30, MinimumLength = 3)]
    [RegularExpression("^[a-z0-9][a-z0-9_-]{1,28}[a-z0-9]$", ErrorMessage = "Use 3-30 lower-case letters, digits, '-' or '_'.")]
    public string Handle { get; set; } = "";

    [StringLength(500)]
    public string? Bio { get; set; }

    [StringLength(1000)]
    [Url(ErrorMessage = "Enter a full https:// image address.")]
    public string? ProfileImageUrl { get; set; }
}

public sealed class PreferencesFormModel
{
    [StringLength(3)]
    public string? PreferredLanguage { get; set; }

    [Required]
    [StringLength(64)]
    public string TimeZoneId { get; set; } = "UTC";

    public bool IsProfilePublic { get; set; }

    public bool IsLibraryPublic { get; set; }
}

public static class SelectOptions
{
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        ("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"), ("it", "Italian"), ("pt", "Portuguese"),
        ("nl", "Dutch"), ("sv", "Swedish"), ("pl", "Polish"), ("tr", "Turkish"), ("ru", "Russian"), ("uk", "Ukrainian"),
        ("ar", "Arabic"), ("fa", "Persian"), ("hi", "Hindi"), ("zh", "Chinese"), ("ja", "Japanese"), ("ko", "Korean"),
    ];

    public static readonly IReadOnlyList<string> TimeZones =
    [
        "UTC", "Europe/London", "Europe/Dublin", "Europe/Lisbon", "Europe/Paris", "Europe/Berlin", "Europe/Madrid", "Europe/Rome",
        "Europe/Amsterdam", "Europe/Stockholm", "Europe/Warsaw", "Europe/Athens", "Europe/Istanbul", "Europe/Kyiv", "Europe/Moscow",
        "Africa/Cairo", "Africa/Lagos", "Africa/Nairobi", "Africa/Johannesburg", "Asia/Dubai", "Asia/Tehran", "Asia/Karachi",
        "Asia/Kolkata", "Asia/Dhaka", "Asia/Bangkok", "Asia/Jakarta", "Asia/Singapore", "Asia/Shanghai", "Asia/Hong_Kong",
        "Asia/Tokyo", "Asia/Seoul", "Australia/Perth", "Australia/Adelaide", "Australia/Sydney", "Pacific/Auckland",
        "America/Sao_Paulo", "America/Buenos_Aires", "America/Bogota", "America/Mexico_City", "America/Halifax",
        "America/New_York", "America/Chicago", "America/Denver", "America/Phoenix", "America/Los_Angeles", "America/Anchorage",
        "Pacific/Honolulu",
    ];
}
