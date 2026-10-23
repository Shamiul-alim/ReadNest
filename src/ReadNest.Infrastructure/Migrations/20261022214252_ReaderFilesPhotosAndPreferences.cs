using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReadNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReaderFilesPhotosAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing profiles get the same reader defaults as new ones.
            migrationBuilder.AddColumn<string>(
                name: "PhotoKey",
                table: "UserProfiles",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PhotoVersion",
                table: "UserProfiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ReaderFlow",
                table: "UserProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Paginated");

            migrationBuilder.AddColumn<int>(
                name: "ReaderFontScale",
                table: "UserProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<int>(
                name: "ReaderLineHeight",
                table: "UserProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 160);

            migrationBuilder.AddColumn<string>(
                name: "ReaderTheme",
                table: "UserProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Paper");

            migrationBuilder.AddColumn<string>(
                name: "ReaderWidth",
                table: "UserProfiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Medium");

            migrationBuilder.CreateTable(
                name: "ReadingFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserBookId = table.Column<Guid>(type: "uuid", nullable: false),
                    Format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SourceNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastLocation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LastPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    LastPage = table.Column<int>(type: "integer", nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    LastOpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PositionUpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingFiles", x => x.Id);
                    table.CheckConstraint("CK_ReadingFiles_Page", "\"LastPage\" IS NULL OR \"LastPage\" >= 1");
                    table.CheckConstraint("CK_ReadingFiles_Percent", "\"LastPercent\" IS NULL OR \"LastPercent\" BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_ReadingFiles_Size", "\"SizeBytes\" > 0");
                    table.ForeignKey(
                        name: "FK_ReadingFiles_UserBooks_UserBookId",
                        column: x => x.UserBookId,
                        principalTable: "UserBooks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingFiles_StorageKey",
                table: "ReadingFiles",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingFiles_UserBookId",
                table: "ReadingFiles",
                column: "UserBookId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadingFiles");

            migrationBuilder.DropColumn(
                name: "PhotoKey",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "PhotoVersion",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ReaderFlow",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ReaderFontScale",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ReaderLineHeight",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ReaderTheme",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "ReaderWidth",
                table: "UserProfiles");
        }
    }
}
