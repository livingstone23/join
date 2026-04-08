using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Commands;

/// <summary>
/// Command to perform a soft delete for a province catalog item.
/// </summary>
/// <param name="Id">The province identifier to delete.</param>
public record DeleteProvinceCommand(Guid Id) : IRequest<Response<Guid>>;