using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReadNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    SecurityStamp = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "text", nullable: true),
                    PhoneNumber = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Authors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimType = table.Column<string>(type: "text", nullable: true),
                    ClaimValue = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserPasskeys",
                columns: table => new
                {
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserPasskeys", x => x.CredentialId);
                    table.ForeignKey(
                        name: "FK_AspNetUserPasskeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LoginProvider = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    NormalizedTitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Subtitle = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Isbn10 = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Isbn13 = table.Column<string>(type: "character varying(13)", maxLength: 13, nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    LanguageCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    PublishedDate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PublishedYear = table.Column<int>(type: "integer", nullable: true),
                    Publisher = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CoverImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    InfoUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                    table.CheckConstraint("CK_Books_ManualOwner", "\"Origin\" <> 'Manual' OR \"CreatedByUserId\" IS NOT NULL");
                    table.CheckConstraint("CK_Books_PageCount", "\"PageCount\" IS NULL OR \"PageCount\" > 0");
                    table.ForeignKey(
                        name: "FK_Books_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomShelves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    ShareSlug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomShelves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomShelves_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    TargetBooks = table.Column<int>(type: "integer", nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingChallenges", x => x.Id);
                    table.CheckConstraint("CK_ReadingChallenges_Dates", "\"EndsOn\" >= \"StartsOn\"");
                    table.CheckConstraint("CK_ReadingChallenges_Target", "\"TargetBooks\" > 0");
                    table.ForeignKey(
                        name: "FK_ReadingChallenges_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TargetBooks = table.Column<int>(type: "integer", nullable: false),
                    TargetPages = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingGoals", x => x.Id);
                    table.CheckConstraint("CK_ReadingGoals_Targets", "\"TargetBooks\" > 0 AND (\"TargetPages\" IS NULL OR \"TargetPages\" > 0)");
                    table.CheckConstraint("CK_ReadingGoals_Year", "\"Year\" BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "FK_ReadingGoals_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserFollows",
                columns: table => new
                {
                    FollowerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolloweeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFollows", x => new { x.FollowerId, x.FolloweeId });
                    table.CheckConstraint("CK_UserFollows_NoSelfFollow", "\"FollowerId\" <> \"FolloweeId\"");
                    table.ForeignKey(
                        name: "FK_UserFollows_AspNetUsers_FolloweeId",
                        column: x => x.FolloweeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFollows_AspNetUsers_FollowerId",
                        column: x => x.FollowerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Handle = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Bio = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProfileImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    TimeZoneId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsProfilePublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsLibraryPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookAuthors",
                columns: table => new
                {
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAuthors", x => new { x.BookId, x.AuthorId });
                    table.ForeignKey(
                        name: "FK_BookAuthors_Authors_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BookAuthors_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookExternalIds",
                columns: table => new
                {
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookExternalIds", x => new { x.Provider, x.ExternalId });
                    table.ForeignKey(
                        name: "FK_BookExternalIds_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookGenres",
                columns: table => new
                {
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    GenreId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookGenres", x => new { x.BookId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_BookGenres_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CurrentPage = table.Column<int>(type: "integer", nullable: false),
                    TotalPages = table.Column<int>(type: "integer", nullable: true),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastReadOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    Review = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    ReviewVisibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReviewUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFavorite = table.Column<bool>(type: "boolean", nullable: false),
                    PrivateNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Ownership = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Format = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AcquiredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    ConditionNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBooks", x => x.Id);
                    table.CheckConstraint("CK_UserBooks_CompletedRequiresRead", "\"CompletedOn\" IS NULL OR \"Status\" = 'Read'");
                    table.CheckConstraint("CK_UserBooks_CurrentPage", "\"CurrentPage\" >= 0");
                    table.CheckConstraint("CK_UserBooks_PublicReviewHasText", "\"ReviewVisibility\" = 'Private' OR \"Review\" IS NOT NULL");
                    table.CheckConstraint("CK_UserBooks_PurchasePrice", "\"PurchasePrice\" IS NULL OR \"PurchasePrice\" >= 0");
                    table.CheckConstraint("CK_UserBooks_Rating", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_UserBooks_StartBeforeCompletion", "\"StartedOn\" IS NULL OR \"CompletedOn\" IS NULL OR \"StartedOn\" <= \"CompletedOn\"");
                    table.CheckConstraint("CK_UserBooks_TotalPages", "\"TotalPages\" IS NULL OR (\"TotalPages\" > 0 AND \"CurrentPage\" <= \"TotalPages\")");
                    table.ForeignKey(
                        name: "FK_UserBooks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserBooks_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChallengeParticipants",
                columns: table => new
                {
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeParticipants", x => new { x.ChallengeId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChallengeParticipants_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChallengeParticipants_ReadingChallenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "ReadingChallenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookLoans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    BorrowerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    LoanedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReturnedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookLoans", x => x.Id);
                    table.CheckConstraint("CK_BookLoans_DueAfterLoan", "\"DueOn\" IS NULL OR \"DueOn\" >= \"LoanedOn\"");
                    table.CheckConstraint("CK_BookLoans_ReturnAfterLoan", "\"ReturnedOn\" IS NULL OR \"ReturnedOn\" >= \"LoanedOn\"");
                    table.ForeignKey(
                        name: "FK_BookLoans_UserBooks_UserBookId",
                        column: x => x.UserBookId,
                        principalTable: "UserBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomShelfBooks",
                columns: table => new
                {
                    ShelfId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomShelfBooks", x => new { x.ShelfId, x.UserBookId });
                    table.ForeignKey(
                        name: "FK_CustomShelfBooks_CustomShelves_ShelfId",
                        column: x => x.ShelfId,
                        principalTable: "CustomShelves",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomShelfBooks_UserBooks_UserBookId",
                        column: x => x.UserBookId,
                        principalTable: "UserBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Content = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    Chapter = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingNotes", x => x.Id);
                    table.CheckConstraint("CK_ReadingNotes_Page", "\"PageNumber\" IS NULL OR \"PageNumber\" >= 0");
                    table.ForeignKey(
                        name: "FK_ReadingNotes_UserBooks_UserBookId",
                        column: x => x.UserBookId,
                        principalTable: "UserBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadOn = table.Column<DateOnly>(type: "date", nullable: false),
                    StartPage = table.Column<int>(type: "integer", nullable: false),
                    EndPage = table.Column<int>(type: "integer", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Reflection = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingSessions", x => x.Id);
                    table.CheckConstraint("CK_ReadingSessions_Duration", "\"DurationMinutes\" BETWEEN 0 AND 1440");
                    table.CheckConstraint("CK_ReadingSessions_Pages", "\"StartPage\" >= 0 AND \"EndPage\" >= \"StartPage\"");
                    table.ForeignKey(
                        name: "FK_ReadingSessions_UserBooks_UserBookId",
                        column: x => x.UserBookId,
                        principalTable: "UserBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("0f580aaf-fd67-5d0f-7264-632190a5a90e"), "Travel", "TRAVEL" },
                    { new Guid("165990b2-7002-320d-4915-acb671bc473e"), "Computers", "COMPUTERS" },
                    { new Guid("17d56480-b711-0bb1-4abf-e14067e31b76"), "Biography", "BIOGRAPHY" },
                    { new Guid("1a1c1e61-8eba-680b-017b-30e7bf18a22d"), "Horror", "HORROR" },
                    { new Guid("1e13a2e7-ee07-eae8-36a7-3cc8301845bb"), "Religion", "RELIGION" },
                    { new Guid("1e841b32-cb76-355a-328a-d18528877d09"), "Children's", "CHILDREN S" },
                    { new Guid("28fae95a-8757-27bb-0d44-6463e43420cd"), "Philosophy", "PHILOSOPHY" },
                    { new Guid("333dc580-e19d-ef2e-25af-ee4b620878a3"), "Design", "DESIGN" },
                    { new Guid("34c08f44-ff59-c90c-178c-9eb1818cf940"), "Drama", "DRAMA" },
                    { new Guid("36e67c4c-f698-fead-0760-5ee8304ab0f4"), "Graphic Novels", "GRAPHIC NOVELS" },
                    { new Guid("3dfc0c18-c26c-de67-2837-ca6f5ff32373"), "Science", "SCIENCE" },
                    { new Guid("45cc365a-d0e2-fb49-184a-b877ec69e344"), "Music", "MUSIC" },
                    { new Guid("4899c5cf-b4ae-07f2-349c-7275fbd75f54"), "Young Adult", "YOUNG ADULT" },
                    { new Guid("4dfb95ea-3e84-674f-fef4-680785c35604"), "Software Engineering", "SOFTWARE ENGINEERING" },
                    { new Guid("574f07e9-f15a-729e-0c62-423103df253a"), "Economics", "ECONOMICS" },
                    { new Guid("5e54324c-382e-3da9-2c40-8de0b74a7e13"), "Art", "ART" },
                    { new Guid("65be9a8e-aca3-6aa6-1486-cd706349871b"), "Science Fiction", "SCIENCE FICTION" },
                    { new Guid("73886009-99c2-f6e5-d51e-543aa9578795"), "Thriller", "THRILLER" },
                    { new Guid("74b3d8dd-f446-2822-d7f5-46611b7e65b7"), "Education", "EDUCATION" },
                    { new Guid("78a5b340-c304-c854-3442-8f49657d2e62"), "Technology", "TECHNOLOGY" },
                    { new Guid("7a35e0b0-62ff-e8c2-ad16-7a5f2f55988f"), "Historical Fiction", "HISTORICAL FICTION" },
                    { new Guid("7fc06fb6-33d9-3868-0442-1c73b3ffc41a"), "Mystery", "MYSTERY" },
                    { new Guid("8ddb1e54-ffa6-2274-da07-c1cbccbe0c9a"), "Fantasy", "FANTASY" },
                    { new Guid("957e8d8c-34bb-59af-535a-fa1ec41d569a"), "History", "HISTORY" },
                    { new Guid("96b1285c-e584-ebd1-db63-ac6cc7d7043d"), "Nature", "NATURE" },
                    { new Guid("98604ac7-8576-08da-d03b-4c18c5d130f8"), "Mathematics", "MATHEMATICS" },
                    { new Guid("991f9d53-68e7-852b-5f93-01f5daf49b72"), "Politics", "POLITICS" },
                    { new Guid("9c659072-146f-b9c1-8195-0f17389d49c0"), "Personal Development", "PERSONAL DEVELOPMENT" },
                    { new Guid("a078ceea-9e15-f6df-46f7-4a1a33c13f68"), "Programming", "PROGRAMMING" },
                    { new Guid("a713ee37-ed1d-866b-a2f0-4fc0cc19f7b1"), "Fiction", "FICTION" },
                    { new Guid("ab4fb199-d969-0145-99ac-6c3af796d2e6"), "Literary Fiction", "LITERARY FICTION" },
                    { new Guid("abd9b51e-fed1-0f60-1ab0-113744006447"), "Self-Help", "SELF HELP" },
                    { new Guid("aeac3830-ab18-d5dc-01a3-c6b74051458d"), "Business", "BUSINESS" },
                    { new Guid("b44d9761-5374-07d3-ea6e-9779194e4ae4"), "Health", "HEALTH" },
                    { new Guid("bb523d08-8d19-6a30-0b1c-a3e5772a83b8"), "Psychology", "PSYCHOLOGY" },
                    { new Guid("bf17ac81-5f3c-c378-0095-ccfc4930ce32"), "Cooking", "COOKING" },
                    { new Guid("c2db5e31-fe56-fce2-4864-cfc7b7cb9d7a"), "Sports", "SPORTS" },
                    { new Guid("cbf7bd11-5a3a-f7b4-c65f-9e7d32b6f936"), "Memoir", "MEMOIR" },
                    { new Guid("cdef1e28-d20b-dd91-28be-97566dbd609c"), "Romance", "ROMANCE" },
                    { new Guid("d8a165e6-9a39-86b6-ce81-e75390ba1203"), "Poetry", "POETRY" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserPasskeys_UserId",
                table: "AspNetUserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Authors_NormalizedName",
                table: "Authors",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookAuthors_AuthorId",
                table: "BookAuthors",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_BookExternalIds_BookId",
                table: "BookExternalIds",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_BookGenres_GenreId",
                table: "BookGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_BookLoans_Outstanding",
                table: "BookLoans",
                column: "UserBookId",
                unique: true,
                filter: "\"ReturnedOn\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BookLoans_UserBookId_LoanedOn",
                table: "BookLoans",
                columns: new[] { "UserBookId", "LoanedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Books_CreatedByUserId",
                table: "Books",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_Isbn13",
                table: "Books",
                column: "Isbn13",
                unique: true,
                filter: "\"Isbn13\" IS NOT NULL AND \"Origin\" = 'Catalog'");

            migrationBuilder.CreateIndex(
                name: "IX_Books_NormalizedTitle",
                table: "Books",
                column: "NormalizedTitle");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipants_UserId",
                table: "ChallengeParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomShelfBooks_UserBookId",
                table: "CustomShelfBooks",
                column: "UserBookId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomShelves_ShareSlug",
                table: "CustomShelves",
                column: "ShareSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomShelves_UserId_NormalizedName",
                table: "CustomShelves",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_NormalizedName",
                table: "Genres",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingChallenges_IsPublic_EndsOn",
                table: "ReadingChallenges",
                columns: new[] { "IsPublic", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingChallenges_OwnerUserId",
                table: "ReadingChallenges",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingGoals_UserId_Year",
                table: "ReadingGoals",
                columns: new[] { "UserId", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingNotes_UserBookId_UpdatedAtUtc",
                table: "ReadingNotes",
                columns: new[] { "UserBookId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_ReadOn",
                table: "ReadingSessions",
                column: "ReadOn");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_UserBookId_ReadOn_StartPage_EndPage",
                table: "ReadingSessions",
                columns: new[] { "UserBookId", "ReadOn", "StartPage", "EndPage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_BookId",
                table: "UserBooks",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_AddedAtUtc",
                table: "UserBooks",
                columns: new[] { "UserId", "AddedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_BookId",
                table: "UserBooks",
                columns: new[] { "UserId", "BookId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_CompletedOn",
                table: "UserBooks",
                columns: new[] { "UserId", "CompletedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_UserBooks_UserId_Status_UpdatedAtUtc",
                table: "UserBooks",
                columns: new[] { "UserId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserFollows_FolloweeId",
                table: "UserFollows",
                column: "FolloweeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_Handle",
                table: "UserProfiles",
                column: "Handle",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserPasskeys");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "BookAuthors");

            migrationBuilder.DropTable(
                name: "BookExternalIds");

            migrationBuilder.DropTable(
                name: "BookGenres");

            migrationBuilder.DropTable(
                name: "BookLoans");

            migrationBuilder.DropTable(
                name: "ChallengeParticipants");

            migrationBuilder.DropTable(
                name: "CustomShelfBooks");

            migrationBuilder.DropTable(
                name: "ReadingGoals");

            migrationBuilder.DropTable(
                name: "ReadingNotes");

            migrationBuilder.DropTable(
                name: "ReadingSessions");

            migrationBuilder.DropTable(
                name: "UserFollows");

            migrationBuilder.DropTable(
                name: "UserProfiles");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Authors");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "ReadingChallenges");

            migrationBuilder.DropTable(
                name: "CustomShelves");

            migrationBuilder.DropTable(
                name: "UserBooks");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
