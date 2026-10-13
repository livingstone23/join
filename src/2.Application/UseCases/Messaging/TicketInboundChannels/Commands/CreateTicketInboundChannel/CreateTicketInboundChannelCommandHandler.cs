using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;

/// <summary>
/// Handles <see cref="CreateTicketInboundChannelCommand"/> (SPEC 47 F2): the channel must exist, be active and have a
/// <c>Code</c> handled by an inbound webhook (<c>WHATSAPP</c>, <c>SENDGRID</c>), and the company may have at most one
/// active row per channel — rotating a token is delete + create. A concurrent create that loses the race on the
/// filtered unique index is answered with the same 409.
/// </summary>
public sealed class CreateTicketInboundChannelCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    IDatabaseErrorClassifier errorClassifier)
    : IRequestHandler<CreateTicketInboundChannelCommand, Response<CreatedTicketInboundChannelDto>>
{
    /// <inheritdoc />
    public async Task<Response<CreatedTicketInboundChannelDto>> Handle(
        CreateTicketInboundChannelCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CreatedTicketInboundChannelDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var channel = await unitOfWork.GetRepository<CommunicationChannel>().GetAsync(request.ChannelId);
        if (channel is null || !channel.IsActive || channel.GcRecord != BaseAuditableEntity.ActiveGcRecord)
        {
            return Response<CreatedTicketInboundChannelDto>.Error(
                TicketInboundErrorCodes.InvalidChannel,
                ["The specified communication channel does not exist or is inactive."]);
        }

        // Routing is always by Code, never by Name (SPEC 47 D).
        if (channel.Code is not (CommunicationChannelCodes.WhatsApp or CommunicationChannelCodes.SendGrid))
        {
            return Response<CreatedTicketInboundChannelDto>.Error(
                TicketInboundErrorCodes.UnsupportedChannel,
                [$"Communication channel '{channel.Code}' is not supported for inbound ingestion."]);
        }

        var repository = unitOfWork.GetRepository<TicketInboundChannel>();
        var activeExists = (await repository.GetAllIncludingDeletedAsync(x =>
                x.CompanyId == companyId && x.ChannelId == request.ChannelId && x.GcRecord == BaseAuditableEntity.ActiveGcRecord))
            .Any();

        if (activeExists)
        {
            return AlreadyActive();
        }

        var entity = new TicketInboundChannel
        {
            CompanyId = companyId,
            ChannelId = request.ChannelId,
            WebhookToken = WebhookTokenGenerator.Generate(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim()
        };

        try
        {
            await repository.InsertAsync(entity);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (errorClassifier.IsUniqueViolation(exception, TicketInboundErrorCodes.CompanyChannelIndex))
        {
            return AlreadyActive();
        }

        return new Response<CreatedTicketInboundChannelDto>
        {
            IsSuccess = true,
            Message = "Ticket inbound channel created successfully.",
            Data = new CreatedTicketInboundChannelDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                ChannelId = entity.ChannelId,
                ChannelCode = channel.Code,
                ChannelName = channel.Name,
                Description = entity.Description,
                WebhookToken = entity.WebhookToken,
                WebhookUrl = string.Empty, // built by the controller from the request host
                CreatedAt = entity.Created
            }
        };
    }

    private static Response<CreatedTicketInboundChannelDto> AlreadyActive() =>
        Response<CreatedTicketInboundChannelDto>.Error(
            TicketInboundErrorCodes.AlreadyActive,
            ["An active inbound channel already exists for this company and communication channel."]);
}
