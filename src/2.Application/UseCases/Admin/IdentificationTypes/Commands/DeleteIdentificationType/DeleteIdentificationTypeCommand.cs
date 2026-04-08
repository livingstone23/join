using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Command used to soft delete an existing identification type.
/// </summary>
/// <param name="Id">The unique identifier of the identification type to delete.</param>
public sealed record DeleteIdentificationTypeCommand(Guid Id) : IRequest<Response<Guid>>;