using CarnotCycleCircus.Core.Domain.Quotes;
using Shouldly;
using Xunit;

namespace CarnotCycleCircus.Tests;

public class ComedyQuoteServiceTests
{
    private readonly ComedyQuoteService _quoteService = new();

    [Fact]
    public void GetAllQuotes_ShouldContainQuotesFromAllCultClassics()
    {
        var quotes = _quoteService.GetAllQuotes();
        quotes.ShouldNotBeEmpty();
        quotes.Count.ShouldBeGreaterThanOrEqualTo(40);

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
            quotes.ShouldContain(q => q.Movie.Contains(movie, StringComparison.OrdinalIgnoreCase),
                $"ComedyQuoteService must include quotes from {movie}");
        }
    }

    [Fact]
    public void GetRandomQuote_ShouldReturnValidQuote()
    {
        var quote = _quoteService.GetRandomQuote();
        quote.ShouldNotBeNull();
        quote.Quote.ShouldNotBeNullOrWhiteSpace();
        quote.Movie.ShouldNotBeNullOrWhiteSpace();
        quote.Character.ShouldNotBeNullOrWhiteSpace();
        quote.Category.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetQuotesByMovie_WithSpaceballs_ShouldReturnSpaceballsQuotes()
    {
        var quotes = _quoteService.GetQuotesByMovie("Spaceballs");
        quotes.ShouldNotBeEmpty();
        quotes.ShouldContain(q => q.Quote.Contains("Ludicrous speed"));
    }

    [Fact]
    public void GetQuotesByCategory_ShouldFilterCorrectly()
    {
        var quotes = _quoteService.GetQuotesByCategory("Velocity");
        quotes.ShouldNotBeEmpty();
        quotes.ShouldAllBe(q => q.Category.Contains("Velocity"));
    }
}
