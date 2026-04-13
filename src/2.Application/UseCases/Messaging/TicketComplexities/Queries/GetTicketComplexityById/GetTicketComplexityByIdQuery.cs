using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Queries;

/// <summary>
/// Query to retrieve a ticket complexity catalog item by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the ticket complexity to retrieve.</param>
public sealed record GetTicketComplexityByIdQuery(Guid Id) : IRequest<Response<TicketComplexityDto>>;
