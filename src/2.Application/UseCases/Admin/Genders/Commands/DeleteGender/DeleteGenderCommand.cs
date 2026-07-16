using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Command used to delete an existing tenant-scoped gender catalog entry.
/// </summary>
/// <param name="Id">The unique identifier of the gender to delete.</param>
public sealed record DeleteGenderCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
