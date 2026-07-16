using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonFinancialProfiles.Commands;

/// <summary>
/// Command that performs a soft delete for a person financial profile in the current tenant.
/// </summary>
/// <param name="Id">The unique identifier of the person financial profile record to delete.</param>
public sealed record DeletePersonFinancialProfileCommand(Guid Id) : ITransactionalCommand<Response<bool>>;
