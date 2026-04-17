using FindThatBook.Core.Matching;
using FindThatBook.Core.Matching.Rules;
using FindThatBook.Core.Models;
using FindThatBook.Core.Ports;
using FindThatBook.Core.UseCases;
using FindThatBook.Core.Validation;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FindThatBook.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MatchingOptions>(configuration.GetSection(MatchingOptions.SectionName));

        services.AddSingleton<ITextNormalizer, TextNormalizer>();

        services.AddSingleton<IMatchRule, ExactTitlePrimaryAuthorRule>();
        services.AddSingleton<IMatchRule, ExactTitleContributorAuthorRule>();
        services.AddSingleton<IMatchRule, NearTitleAuthorRule>();
        services.AddSingleton<IMatchRule, AuthorOnlyFallbackRule>();
        services.AddSingleton<IMatchRule, WeakMatchRule>();

        services.AddSingleton<IBookMatcher, BookMatcher>();

        services.AddScoped<FindBookQueryHandler>();

        services.AddValidatorsFromAssemblyContaining<FindBookRequestValidator>();

        return services;
    }
}
