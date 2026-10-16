using ReadNest.Domain.ValueObjects;

namespace ReadNest.UnitTests.Domain;

public sealed class IsbnTests
{
    [Theory]
    [InlineData("978-0-13-235088-4", "9780132350884")]
    [InlineData("0132350882", "9780132350884")]
    [InlineData("0-8044-2957-X", "9780804429573")]
    [InlineData(" 9780132350884 ", "9780132350884")]
    public void Normalizes_valid_isbns_to_isbn13(string input, string expected) =>
        Assert.Equal(expected, Isbn.NormalizeToIsbn13(input));

    [Theory]
    [InlineData("9780132350885")]
    [InlineData("0132350883")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("97801323508841234567890123456789012")]
    public void Rejects_invalid_isbns(string? input) => Assert.Null(Isbn.NormalizeToIsbn13(input));

    [Fact]
    public void Converts_978_isbn13_back_to_isbn10()
    {
        Assert.Equal("0132350882", Isbn.ToIsbn10("9780132350884"));
        Assert.Null(Isbn.ToIsbn10("9791032305690"));
    }

    [Theory]
    [InlineData("Café  Society", "CAFE SOCIETY")]
    [InlineData("  The Hobbit: or There and Back Again ", "THE HOBBIT OR THERE AND BACK AGAIN")]
    [InlineData("J. R. R. Tolkien", "J R R TOLKIEN")]
    public void Text_keys_ignore_case_accents_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.Key(input));
}
