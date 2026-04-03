using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Command to perform a soft delete for a company.
/// </summary>
/// <param name="Id">The company identifier to delete.</param>
public record DeleteCompanyCommand(Guid Id) : IRequest<Response<Guid>>;
