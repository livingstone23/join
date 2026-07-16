using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

/// <summary>
/// Command used to perform a logical delete of a tenant ticket default configuration.
/// </summary>
/// <param name="Id">Configuration identifier.</param>
public record DeleteTicketCompanyDefaultCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
