using ReadNest.Application.ImportExport;
using ReadNest.Domain.Enums;

namespace ReadNest.UnitTests.Application;

public sealed class CsvTests
{
    private static readonly DateOnly Today = new(2026, 10, 16);

    [Fact]
    public void Parses_quotes_commas_newlines_and_bom()
    {
        var records = CsvReader.Parse("﻿Title,Author\r\n\"Hello, World\",\"Line1\nLine2\"\r\n\"He said \"\"hi\"\"\",X\n", 100);

        Assert.Equal(3, records.Count);
        Assert.Equal(["Title", "Author"], records[0]);
        Assert.Equal("Hello, World", records[1][0]);
        Assert.Equal("Line1\nLine2", records[1][1]);
        Assert.Equal("He said \"hi\"", records[2][0]);
    }

    [Fact]
    public void Rejects_unterminated_quotes()
    {
        Assert.Throws<CsvFormatException>(() => CsvReader.Parse("Title\n\"broken", 10));
    }

    [Fact]
    public void Enforces_the_row_limit()
    {
        var content = "Title\n" + string.Join("\n", Enumerable.Range(0, 20).Select(i => "Book " + i));

        Assert.Throws<CsvFormatException>(() => CsvReader.Parse(content, 10));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("+SUM(A1)", "\"'+SUM(A1)\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@cmd", "\"'@cmd\"")]
    [InlineData("Plain title", "Plain title")]
    [InlineData("Comma, inside", "\"Comma, inside\"")]
    public void Writer_neutralises_formula_injection(string value, string expected) =>
        Assert.Equal(expected, CsvWriter.Escape(value));

    [Fact]
    public void Round_trips_an_exported_formula_like_title()
    {
        var line = CsvWriter.Line(["=1+1", "Author"]);
        var records = CsvReader.Parse("Title,Author\n" + line, 10);
        var header = LibraryCsvFormat.ParseHeader(records[0]);

        var (row, errors) = LibraryCsvFormat.ValidateRow(header, records[1], 2, Today);

        Assert.Empty(errors);
        Assert.Equal("=1+1", row!.Title);
    }

    [Fact]
    public void Validates_a_complete_row()
    {
        var header = LibraryCsvFormat.ParseHeader(["title", "AUTHOR", "isbn", "Status", "CurrentPage", "TotalPages", "Rating", "StartedOn", "CompletedOn", "Favorite", "Shelves"]);
        string[] record = ["Clean Code", "Robert C. Martin; Someone Else", "0132350882", "read", "", "464", "5", "2026-01-02", "2026-02-03", "yes", "Software; Favourites"];

        var (row, errors) = LibraryCsvFormat.ValidateRow(header, record, 2, Today);

        Assert.Empty(errors);
        Assert.NotNull(row);
        Assert.Equal(ReadingStatus.Read, row.Status);
        Assert.Equal("9780132350884", row.Isbn13);
        Assert.Equal(["Robert C. Martin", "Someone Else"], row.Authors);
        Assert.True(row.IsFavorite);
        Assert.Equal(2, row.Shelves.Count);
    }

    [Theory]
    [InlineData("Rating", "9", "Rating must be a whole number between 1 and 5.")]
    [InlineData("Rating", "four", "Rating must be a whole number between 1 and 5.")]
    [InlineData("CurrentPage", "-4", "CurrentPage must be a whole number between 0 and 100000.")]
    [InlineData("ISBN", "123", "'123' is not a valid ISBN-10 or ISBN-13.")]
    [InlineData("Status", "Skimmed", "Unknown status 'Skimmed'.")]
    [InlineData("StartedOn", "16/10/2026", "StartedOn must be a date in the format yyyy-MM-dd.")]
    [InlineData("StartedOn", "2099-01-01", "StartedOn cannot be in the future.")]
    public void Reports_invalid_values(string column, string value, string expectedStart)
    {
        var header = LibraryCsvFormat.ParseHeader(["Title", column]);

        var (row, errors) = LibraryCsvFormat.ValidateRow(header, ["Some Book", value], 2, Today);

        Assert.Null(row);
        Assert.Contains(errors, e => e.StartsWith(expectedStart, StringComparison.Ordinal));
    }

    [Fact]
    public void Requires_a_title_and_consistent_dates()
    {
        var header = LibraryCsvFormat.ParseHeader(["Title", "Status", "CompletedOn", "CurrentPage", "TotalPages"]);

        var (_, missingTitle) = LibraryCsvFormat.ValidateRow(header, ["", "", "", "", ""], 2, Today);
        var (_, wrongStatus) = LibraryCsvFormat.ValidateRow(header, ["A", "Want to Read", "2026-01-01", "", ""], 3, Today);
        var (_, pages) = LibraryCsvFormat.ValidateRow(header, ["A", "", "", "500", "400"], 4, Today);

        Assert.Contains("Title is required.", missingTitle);
        Assert.Contains("CompletedOn can only be set when Status is Read.", wrongStatus);
        Assert.Contains("CurrentPage cannot be greater than TotalPages.", pages);
    }
}
