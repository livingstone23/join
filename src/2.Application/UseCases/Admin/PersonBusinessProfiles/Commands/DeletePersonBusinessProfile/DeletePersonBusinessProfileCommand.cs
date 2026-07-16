using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonBusinessProfiles.Commands;

/// <summary>
/// Command that performs a soft delete for a person business profile in the current tenant.
/// </summary>
/// <param name="Id">The unique identifier of the person business profile record to delete.</param>
public sealed record DeletePersonBusinessProfileCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
