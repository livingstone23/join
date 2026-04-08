using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Command used to perform a soft delete over a tenant-scoped area.
/// </summary>
/// <param name="Id">The unique identifier of the area to delete.</param>
/// <param name="CompanyId">The tenant identifier used to validate the deletion scope.</param>
public sealed record DeleteAreaCommand(Guid Id, Guid CompanyId)
    : IRequest<Response<Guid>>;
