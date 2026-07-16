using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Persons.Commands;

/// <summary>
/// Command to soft-delete an existing customer aggregate.
/// </summary>
/// <param name="Id">The customer identifier to delete.</param>
public record DeletePersonCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
