using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Persons.Queries;

/// <summary>
/// <c>GET /Persons/lookup?term=&amp;limit=</c> (SPEC 54): a single search over the company's persons and customers.
/// </summary>
/// <param name="Term">Free text, at least 2 characters after trimming.</param>
/// <param name="Limit">Maximum rows; default 20, clamped to 1–50.</param>
public sealed record GetPersonLookupQuery(string? Term, int? Limit = null)
    : IRequest<Response<IReadOnlyList<PersonLookupItemDto>>>;
