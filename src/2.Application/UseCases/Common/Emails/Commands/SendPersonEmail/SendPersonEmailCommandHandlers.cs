using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Common.Emails.Commands;

/// <summary>Handles <see cref="SendPersonEmailCommand"/>: <c>Origin = Person</c>, optional lead.</summary>
public sealed class SendPersonEmailCommandHandler(PersonEmailSender sender)
    : IRequestHandler<SendPersonEmailCommand, Response<PersonEmailSentDto>>
{
    public Task<Response<PersonEmailSentDto>> Handle(SendPersonEmailCommand request, CancellationToken cancellationToken) =>
        sender.SendAsync(PersonEmailRequests.From(request, request.PersonId, PersonEmailOrigin.Person, null, request.LeadId), cancellationToken);
}

/// <summary>Handles <see cref="SendCustomerEmailCommand"/>: resolves the person of the customer, <c>Origin = Customer</c>.</summary>
public sealed class SendCustomerEmailCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonEmailSender sender)
    : IRequestHandler<SendCustomerEmailCommand, Response<PersonEmailSentDto>>
{
    public async Task<Response<PersonEmailSentDto>> Handle(SendCustomerEmailCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PersonEmailSentDto>.Error(PersonEmailErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var customer = (await unitOfWork.GetRepository<Customer>().GetAllIncludingDeletedAsync(
            x => x.Id == request.CustomerId && x.CompanyId == currentUserService.CompanyId && x.GcRecord == 0)).FirstOrDefault();
        if (customer is null)
        {
            return Response<PersonEmailSentDto>.Error(PersonEmailErrorCodes.CustomerNotFound, ["Customer not found."]);
        }

        return await sender.SendAsync(
            PersonEmailRequests.From(request, customer.PersonId, PersonEmailOrigin.Customer, customer.Id, request.LeadId), cancellationToken);
    }
}

/// <summary>Handles <see cref="SendLeadEmailCommand"/>: the lead in the scope of the user (SPEC 60) gives the person.</summary>
public sealed class SendLeadEmailCommandHandler(
    LeadCommandContext leadContext,
    PersonEmailSender sender)
    : IRequestHandler<SendLeadEmailCommand, Response<PersonEmailSentDto>>
{
    public async Task<Response<PersonEmailSentDto>> Handle(SendLeadEmailCommand request, CancellationToken cancellationToken)
    {
        if (leadContext.Actor<PersonEmailSentDto>(out var actor) is { } noActor)
        {
            return noActor;
        }

        var (load, error) = await leadContext.LoadAsync<PersonEmailSentDto>(actor, request.LeadId, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        return await sender.SendAsync(
            PersonEmailRequests.From(request, load!.Lead.PersonId, PersonEmailOrigin.Lead, null, load.Lead.Id), cancellationToken);
    }
}

internal static class PersonEmailRequests
{
    internal static PersonEmailSendRequest From(IPersonEmailInput input, Guid personId, PersonEmailOrigin origin, Guid? customerId, Guid? leadId) =>
        new(personId, origin, customerId, leadId, input.ToAddress, input.Cc ?? [], input.Subject, input.BodyHtml, input.EmailTemplateId, input.Attachments ?? []);
}
