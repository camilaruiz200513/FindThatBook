using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindThatBook.Core.Domain;
using FindThatBook.Core.Matching;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using FindThatBook.Tests.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Moq;

namespace FindThatBook.Tests.Api;

public class BooksEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly WebApplicationFactory<Program> _factory;

    public BooksEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private WebApplicationFactory<Program> BuildFactory(
        ExtractedBookInfo hypothesis,
        IReadOnlyList<Book> books) =>
        _factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureServices(services =>
            {
                TestHostHelpers.ReplaceExternalPorts(services, hypothesis, books);
                services.Configure<MatchingOptions>(o => o.UseLlmRerank = false);
            });
        });

    [Fact]
    public async Task Post_find_returns_200_with_candidates()
    {
        var hypothesis = new ExtractedBookInfo("The Hobbit", "Tolkien", 1937, Array.Empty<string>());
        var factory = BuildFactory(hypothesis, new[] { BookFactory.Create() });
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/books/find",
            new FindBookRequest("tolkien hobbit 1937"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<FindBookResponse>(JsonOptions);
        payload.Should().NotBeNull();
        payload!.OriginalQuery.Should().Be("tolkien hobbit 1937");
        payload.Candidates.Should().NotBeEmpty();
        payload.Candidates[0].Tier.Should().Be(MatchTier.Exact);
        response.Headers.Should().Contain(h => h.Key == "X-Correlation-Id");
    }

    [Fact]
    public async Task Post_find_returns_400_for_empty_query()
    {
        var factory = BuildFactory(ExtractedBookInfo.Empty, Array.Empty<Book>());
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/books/find",
            new FindBookRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_health_returns_ok()
    {
        var factory = BuildFactory(ExtractedBookInfo.Empty, Array.Empty<Book>());
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
