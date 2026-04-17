using FindThatBook.Core.Models;
using FluentValidation;

namespace FindThatBook.Core.Validation;

public sealed class FindBookRequestValidator : AbstractValidator<FindBookRequest>
{
    public FindBookRequestValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Query is required.")
            .MaximumLength(500).WithMessage("Query must be at most 500 characters.");

        RuleFor(x => x.MaxResults)
            .InclusiveBetween(1, 20)
            .WithMessage("MaxResults must be between 1 and 20.");
    }
}
