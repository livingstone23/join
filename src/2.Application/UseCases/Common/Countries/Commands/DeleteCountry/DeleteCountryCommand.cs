using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Commands;

/// <summary>
/// Command to perform a soft delete for a country catalog item.
/// </summary>
/// <param name="Id">The country identifier to delete.</param>
public record DeleteCountryCommand(Guid Id) : IRequest<Response<Guid>>;
