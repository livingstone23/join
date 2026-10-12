using System;
using System.Linq;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Messaging.TicketInboundChannels.Common;
using JOIN.Domain.Audit;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketInboundChannels.Commands.CreateTicketInboundChannel;

/// <summary>
/// Maneja la creación de un <c>TicketInboundChannel</c>. Aplica tres reglas antes
/// de tocar la BD:
/// <list type="number">
/// <item><c>ChannelId</c> debe existir y estar activo.</item>
/// <item>Su <c>CommunicationChannel.Code</c> debe ser uno de los soportados para ingesta
/// (<c>WHATSAPP</c>, <c>SENDGRID</c>); el resto devuelve <see cref="TicketInboundErrorCodes.UnsupportedChannel"/>.</item>
/// <item>No debe existir otra fila activa para <c>(CompanyId, ChannelId)</c> — la violación
/// del índice único filtrado se traduce a <see cref="TicketInboundErrorCodes.UnsupportedChannel"/>-equivalente
/// <c>INBOUND_CHANNEL_ALREADY_ACTIVE</c> (409).</item>
/// </list>
/// </summary>
public sealed class CreateTicketInboundChannelCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateTicketInboundChannelCommand, Response<CreatedTicketInboundChannelDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <inheritdoc />
    public async Task<Response<CreatedTicketInboundChannelDto>> Handle(
        CreateTicketInboundChannelCommand request,
        CancellationToken cancellationToken)
    {
        if (!_currentUserService.IsAuthenticated || _currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CreatedTicketInboundChannelDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = _currentUserService.CompanyId;

        var channelRepository = _unitOfWork.GetRepository<CommunicationChannel>();
        var channel = await channelRepository.GetAsync(request.ChannelId);

        if (channel is null || !channel.IsActive || channel.GcRecord != BaseAuditableEntity.ActiveGcRecord)
        {
            return Response<CreatedTicketInboundChannelDto>.Error(
                "INVALID_CHANNEL",
                ["The specified communication channel does not exist or is inactive."]);
        }

        // Drive the spec's "ruteo por Code, nunca por Name" requirement.
        var code = channel.Code;
        if (code is not (CommunicationChannelCodes.WhatsApp or CommunicationChannelCodes.SendGrid))
        {
            return Response<CreatedTicketInboundChannelDto>.Error(
                TicketInboundErrorCodes.UnsupportedChannel,
                [$"Communication channel '{code}' is not supported for inbound ingestion."]);
        }

        var repository = _unitOfWork.GetRepository<TicketInboundChannel>();
        var existing = await repository.GetAllAsync();
        var activeExists = existing.Any(x =>
            x.CompanyId == companyId
            && x.ChannelId == request.ChannelId
            && x.GcRecord == BaseAuditableEntity.ActiveGcRecord);

        if (activeExists)
        {
            return Response<CreatedTicketInboundChannelDto>.Error(
                "INBOUND_CHANNEL_ALREADY_ACTIVE",
                ["An active inbound channel already exists for this company + communication channel."]);
        }

        var entity = new TicketInboundChannel
        {
            CompanyId = companyId,
            ChannelId = request.ChannelId,
            WebhookToken = WebhookTokenGenerator.Generate(),
            Description = request.Description?.Trim()
        };

        await repository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<CreatedTicketInboundChannelDto>.Error(
                "CREATE_FAILED",
                ["No records were affected while creating the inbound channel."]);
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
                ChannelCode = channel.Code ?? string.Empty,
                ChannelName = channel.Name,
                Description = entity.Description,
                WebhookToken = entity.WebhookToken,
                WebhookUrl = string.Empty, // el controller lo construye desde HttpContext
                CreatedAt = entity.Created
            }
        };
    }
}
