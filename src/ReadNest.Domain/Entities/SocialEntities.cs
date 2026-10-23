using System.Text.RegularExpressions;
using ReadNest.Domain.Common;
using ReadNest.Domain.Enums;
using ReadNest.Domain.Exceptions;
using ReadNest.Domain.ValueObjects;

namespace ReadNest.Domain.Entities;

/// <summary>Public-facing profile and reading preferences of a user. Keyed by the Identity user id.</summary>
public sealed partial class UserProfile
{
    public const int DisplayNameMaxLength = 80;
    public const int BioMaxLength = 500;
    public const int HandleMinLength = 3;
    public const int HandleMaxLength = 30;

    private UserProfile()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Lower-case unique name used in public profile URLs.</summary>
    public string Handle { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? Bio { get; private set; }

    public string? ProfileImageUrl { get; private set; }

    /// <summary>ISO 639-1 language code preferred for catalogue searches.</summary>
    public string? PreferredLanguage { get; private set; }

    /// <summary>IANA (or Windows) time zone id used to determine the reader's local "today".</summary>
    public string TimeZoneId { get; private set; } = "UTC";

    public bool IsProfilePublic { get; private set; }

    /// <summary>Whether the reading list (statuses and ratings, never notes) is visible on the public profile.</summary>
    public bool IsLibraryPublic { get; private set; }

    /// <summary>Storage key of the uploaded profile photo (never a path or a user-supplied name).</summary>
    public string? PhotoKey { get; private set; }

    /// <summary>Changes whenever the photo changes, so photo URLs can be cached safely.</summary>
    public long PhotoVersion { get; private set; }

    public ReaderTheme ReaderTheme { get; private set; } = ReaderTheme.Paper;

    /// <summary>Reader text size as a percentage of the default (80-160).</summary>
    public int ReaderFontScale { get; private set; } = 100;

    /// <summary>Reader line height in hundredths (130 means 1.3).</summary>
    public int ReaderLineHeight { get; private set; } = 160;

    public ReaderWidth ReaderWidth { get; private set; } = ReaderWidth.Medium;

    public ReaderFlow ReaderFlow { get; private set; } = ReaderFlow.Paginated;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public const int MinReaderFontScale = 80;
    public const int MaxReaderFontScale = 160;
    public const int MinReaderLineHeight = 120;
    public const int MaxReaderLineHeight = 220;

    /// <summary>
    /// The URL that shows this reader's photo: the uploaded photo (served through an endpoint that checks
    /// visibility) or, for older profiles, an external image link.
    /// </summary>
    public static string? PhotoUrlFor(Guid userId, string? photoKey, long photoVersion, string? externalImageUrl) =>
        photoKey is null ? externalImageUrl : $"api/v1/users/{userId:N}/photo?v={photoVersion}";

    public static bool IsValidHandle(string? handle) => handle is not null && HandleRegex().IsMatch(handle);

    public static UserProfile Create(Guid userId, string handle, string displayName, string timeZoneId, DateTime nowUtc)
    {
        var profile = new UserProfile { UserId = userId, CreatedAtUtc = nowUtc, TimeZoneId = timeZoneId };
        profile.UpdateProfile(displayName, handle, bio: null, profileImageUrl: null, nowUtc);
        return profile;
    }

    public void UpdateProfile(string displayName, string handle, string? bio, string? profileImageUrl, DateTime nowUtc)
    {
        var normalizedHandle = handle.Trim().ToLowerInvariant();
        if (!IsValidHandle(normalizedHandle))
        {
            throw new DomainException(
                $"Handles must be {HandleMinLength}-{HandleMaxLength} characters of lower-case letters, digits, '-' or '_', starting and ending with a letter or digit.",
                "handle");
        }

        DisplayName = TextNormalizer.CollapseWhitespace(Guard.Required(displayName, DisplayNameMaxLength, "displayName", "Display name"));
        Handle = normalizedHandle;
        Bio = Guard.Optional(bio, BioMaxLength, "bio", "Bio");
        ProfileImageUrl = profileImageUrl is { Length: > 1000 }
            ? throw new DomainException("Profile image URL is too long.", "profileImageUrl")
            : profileImageUrl;
        UpdatedAtUtc = nowUtc;
    }

    public void UpdatePreferences(string? preferredLanguage, string timeZoneId, DateTime nowUtc)
    {
        var language = string.IsNullOrWhiteSpace(preferredLanguage) ? null : preferredLanguage.Trim().ToLowerInvariant();
        if (language is not null && !LanguageRegex().IsMatch(language))
        {
            throw new DomainException("Preferred language must be a two- or three-letter ISO language code.", "preferredLanguage");
        }

        PreferredLanguage = language;
        TimeZoneId = Guard.Required(timeZoneId, 64, "timeZoneId", "Time zone");
        UpdatedAtUtc = nowUtc;
    }

    public void UpdatePrivacy(bool isProfilePublic, bool isLibraryPublic, DateTime nowUtc)
    {
        IsProfilePublic = isProfilePublic;

        // A public reading list is shown on the public profile, so it cannot be public on its own.
        IsLibraryPublic = isProfilePublic && isLibraryPublic;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Points the profile at a newly stored photo and returns the key of the photo it replaces.</summary>
    public string? SetPhoto(string photoKey, DateTime nowUtc)
    {
        var previous = PhotoKey;
        PhotoKey = Guard.Required(photoKey, 200, "photoKey", "Photo");
        ProfileImageUrl = null;
        PhotoVersion = nowUtc.Ticks;
        UpdatedAtUtc = nowUtc;
        return previous;
    }

    /// <summary>Removes the photo (uploaded or linked) and returns the key of the stored photo, if any.</summary>
    public string? RemovePhoto(DateTime nowUtc)
    {
        var previous = PhotoKey;
        PhotoKey = null;
        ProfileImageUrl = null;
        PhotoVersion = nowUtc.Ticks;
        UpdatedAtUtc = nowUtc;
        return previous;
    }

    public void UpdateReaderPreferences(ReaderTheme theme, int fontScale, int lineHeight, ReaderWidth width, ReaderFlow flow, DateTime nowUtc)
    {
        if (!Enum.IsDefined(theme) || !Enum.IsDefined(width) || !Enum.IsDefined(flow))
        {
            throw new DomainException("Unknown reader setting.", "readerPreferences");
        }

        ReaderTheme = theme;
        ReaderFontScale = Guard.Range(fontScale, MinReaderFontScale, MaxReaderFontScale, "fontScale", "Text size");
        ReaderLineHeight = Guard.Range(lineHeight, MinReaderLineHeight, MaxReaderLineHeight, "lineHeight", "Line spacing");
        ReaderWidth = width;
        ReaderFlow = flow;
        UpdatedAtUtc = nowUtc;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9_-]{1,28})[a-z0-9]$")]
    private static partial Regex HandleRegex();

    [GeneratedRegex("^[a-z]{2,3}$")]
    private static partial Regex LanguageRegex();
}

/// <summary>A user-defined shelf. Shelves contain library entries, so removing a book from a shelf never removes it from the library.</summary>
public sealed class CustomShelf : Entity
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 500;

    private CustomShelf()
    {
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string NormalizedName { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsPublic { get; private set; }

    /// <summary>Stable random token for the public share link; does not reveal the shelf id.</summary>
    public string ShareSlug { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public List<CustomShelfBook> Entries { get; private set; } = [];

    public static CustomShelf Create(Guid userId, string name, string? description, bool isPublic, string shareSlug, DateTime nowUtc)
    {
        var shelf = new CustomShelf
        {
            UserId = userId,
            ShareSlug = Guard.Required(shareSlug, 40, "shareSlug", "Share slug"),
            CreatedAtUtc = nowUtc,
        };
        shelf.Update(name, description, isPublic, nowUtc);
        return shelf;
    }

    public void Update(string name, string? description, bool isPublic, DateTime nowUtc)
    {
        Name = TextNormalizer.CollapseWhitespace(Guard.Required(name, NameMaxLength, "name", "Shelf name"));
        NormalizedName = TextNormalizer.Key(Name);
        if (NormalizedName.Length == 0)
        {
            throw new DomainException("Shelf name must contain letters or digits.", "name");
        }

        Description = Guard.Optional(description, DescriptionMaxLength, "description", "Description");
        IsPublic = isPublic;
        UpdatedAtUtc = nowUtc;
    }
}

public sealed class CustomShelfBook
{
    public Guid ShelfId { get; set; }

    public CustomShelf Shelf { get; set; } = null!;

    public Guid UserBookId { get; set; }

    public UserBook UserBook { get; set; } = null!;

    public DateTime AddedAtUtc { get; set; }
}

public sealed class UserFollow
{
    private UserFollow()
    {
    }

    public Guid FollowerId { get; private set; }

    public Guid FolloweeId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static UserFollow Create(Guid followerId, Guid followeeId, DateTime nowUtc)
    {
        if (followerId == followeeId)
        {
            throw new DomainException("You cannot follow yourself.");
        }

        return new UserFollow { FollowerId = followerId, FolloweeId = followeeId, CreatedAtUtc = nowUtc };
    }
}

/// <summary>
/// A recommendation the reader marked "Not for me". The book is left out of their suggestions until they restore
/// their dismissed suggestions.
/// </summary>
public sealed class RecommendationDismissal
{
    private RecommendationDismissal()
    {
    }

    public Guid UserId { get; private set; }

    public Guid BookId { get; private set; }

    public DateTime DismissedAtUtc { get; private set; }

    public static RecommendationDismissal Create(Guid userId, Guid bookId, DateTime nowUtc) =>
        new() { UserId = userId, BookId = bookId, DismissedAtUtc = nowUtc };
}

/// <summary>A reading challenge: read <see cref="TargetBooks"/> books between two dates.</summary>
public sealed class ReadingChallenge : Entity
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 1_000;

    private ReadingChallenge()
    {
    }

    public Guid OwnerUserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public int TargetBooks { get; private set; }

    public DateOnly StartsOn { get; private set; }

    public DateOnly EndsOn { get; private set; }

    public bool IsPublic { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public List<ChallengeParticipant> Participants { get; private set; } = [];

    public static ReadingChallenge Create(Guid ownerUserId, string title, string? description, int targetBooks, DateOnly startsOn, DateOnly endsOn, bool isPublic, DateTime nowUtc)
    {
        if (endsOn < startsOn)
        {
            throw new DomainException("The challenge must end on or after its start date.", "endsOn");
        }

        if (endsOn.DayNumber - startsOn.DayNumber > 3 * 366)
        {
            throw new DomainException("A challenge can last at most three years.", "endsOn");
        }

        var challenge = new ReadingChallenge
        {
            OwnerUserId = ownerUserId,
            Title = Guard.Required(title, TitleMaxLength, "title", "Title"),
            Description = Guard.Optional(description, DescriptionMaxLength, "description", "Description"),
            TargetBooks = Guard.Range(targetBooks, 1, 500, "targetBooks", "Target books"),
            StartsOn = startsOn,
            EndsOn = endsOn,
            IsPublic = isPublic,
            CreatedAtUtc = nowUtc,
        };
        challenge.Participants.Add(new ChallengeParticipant { ChallengeId = challenge.Id, UserId = ownerUserId, JoinedAtUtc = nowUtc });
        return challenge;
    }

    public bool IsActive(DateOnly today) => today >= StartsOn && today <= EndsOn;
}

public sealed class ChallengeParticipant
{
    public Guid ChallengeId { get; set; }

    public ReadingChallenge Challenge { get; set; } = null!;

    public Guid UserId { get; set; }

    public DateTime JoinedAtUtc { get; set; }
}
