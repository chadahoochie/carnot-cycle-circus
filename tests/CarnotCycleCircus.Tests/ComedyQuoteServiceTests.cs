using CarnotCycleCircus.Core.Domain.Quotes;
using FluentAssertions;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ComedyQuoteServiceTests
{
    private readonly ComedyQuoteService _quoteService = new();

    [Fact]
    public void GetAllQuotes_ShouldContainQuotesFromAllCultClassics()
    {
        var quotes = _quoteService.GetAllQuotes();
        quotes.Should().NotBeEmpty();
        quotes.Count.Should().BeGreaterThanOrEqualTo(40);

        // Verify quotes from key requested movies
        var expectedMovies = new[]
        {
            "Spaceballs",
            "Monty Python",
            "Ace Ventura",
            "Star Wars",
            "Airplane!",
            "Naked Gun",
            "Kung Pow",
            "Blazing Saddles",
            "Christmas Vacation",
            "Caddyshack",
            "Dumb and Dumber",
            "Robin Hood: Men in Tights",
            "Young Frankenstein",
            "The Jerk",
            "Hot Shots",
            "Tommy Boy",
            "The Waterboy",
            "Old School",
            "Top Secret",
            "Super Troopers",
            "The Fifth Element"
        };

        foreach (var movie in expectedMovies)
        {
            quotes.Should().Contain(q => q.Movie.Contains(movie, StringComparison.OrdinalIgnoreCase),
                because: $"ComedyQuoteService must include quotes from {movie}");
        }
    }

    [Fact]
    public void GetRandomQuote_ShouldReturnValidQuote()
    {
        var quote = _quoteService.GetRandomQuote();
        quote.Should().NotBeNull();
        quote.Quote.Should().NotBeNullOrWhiteSpace();
        quote.Movie.Should().NotBeNullOrWhiteSpace();
        quote.Character.Should().NotBeNullOrWhiteSpace();
        quote.Category.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetQuotesByMovie_WithSpaceballs_ShouldReturnSpaceballsQuotes()
    {
        var quotes = _quoteService.GetQuotesByMovie("Spaceballs");
        quotes.Should().NotBeEmpty();
        quotes.Should().Contain(q => q.Quote.Contains("Ludicrous speed"));
    }

    [Fact]
    public void GetQuotesByCategory_ShouldFilterCorrectly()
    {
        var quotes = _quoteService.GetQuotesByCategory("Velocity");
        quotes.Should().NotBeEmpty();
        quotes.Should().AllSatisfy(q => q.Category.Should().Contain("Velocity"));
    }
}
