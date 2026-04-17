namespace FindThatBook.Core.Models;

public sealed record FindBookRequest(string Query, int MaxResults = 5);
