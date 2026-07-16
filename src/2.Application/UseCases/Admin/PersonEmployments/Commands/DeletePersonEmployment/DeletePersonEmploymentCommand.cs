using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Command that performs a soft delete for a person employment record in the current tenant.
/// </summary>
/// <param name="Id">The unique identifier of the person employment record to delete.</param>
public sealed record DeletePersonEmploymentCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
