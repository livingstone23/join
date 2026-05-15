using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Command used to delete an existing tenant-scoped industry catalog entry.
/// </summary>
/// <param name="Id">The unique identifier of the industry to delete.</param>
public sealed record DeleteIndustryCommand(Guid Id) : IRequest<Response<Guid>>;
