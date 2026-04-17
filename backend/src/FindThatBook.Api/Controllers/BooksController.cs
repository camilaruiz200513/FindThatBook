using FindThatBook.Core.Models;
using FindThatBook.Core.UseCases;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace FindThatBook.Api.Controllers;

[ApiController]
[Route("api/books")]
[Produces("application/json")]
public sealed class BooksController : ControllerBase
{
    private readonly FindBookQueryHandler _handler;
    private readonly IValidator<FindBookRequest> _validator;

    public BooksController(
        FindBookQueryHandler handler,
        IValidator<FindBookRequest> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>
    /// Finds book candidates for a noisy query using LLM extraction and Open Library search.
    /// </summary>
    /// <remarks>
    /// Example query: <c>{ "query": "tolkien hobbit 1937", "maxResults": 5 }</c>
    /// </remarks>
    [HttpPost("find")]
    [ProducesResponseType(typeof(FindBookResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<FindBookResponse>> FindAsync(
        [FromBody] FindBookRequest request,
        CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var response = await _handler.HandleAsync(request, cancellationToken);
        return Ok(response);
    }
}
