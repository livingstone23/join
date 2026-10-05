using System;
using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketStatusTransitions.Commands.DeleteTicketStatusTransition;

/// <summary>
/// Soft-deletes a <see cref="JOIN.Domain.Messaging.TicketStatusTransition"/> rule.
/// No <c>Update</c> endpoint exists by design (SPEC 37): the (From, To) pair is the rule's identity.
/// </summary>
public sealed record DeleteTicketStatusTransitionCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;