using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.Tickets.Commands;

/// <summary>
/// Handles ticket update commands.
/// </summary>
public sealed class UpdateTicketCommandHandler(
    IUnitOfWork unitOfWork,
    ITicketMapper ticketMapper,
    ICurrentUserService currentUserService,
    TicketDtoAssembler ticketDtoAssembler)
    : IRequestHandler<UpdateTicketCommand, Response<TicketDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ITicketMapper _ticketMapper = ticketMapper;
    private readonly TicketDtoAssembler _ticketDtoAssembler = ticketDtoAssembler;

    /// <summary>
    /// Updates an existing ticket in the current tenant context.
    /// </summary>
    public async Task<Response<TicketDto>> Handle(UpdateTicketCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var currentUserId))
        {
            return Response<TicketDto>.Error("USER_REQUIRED", ["The authenticated user identifier is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var company = await companyRepository.GetAsync(currentUserService.CompanyId);
        if (company is null)
        {
            return Response<TicketDto>.Error("INVALID_COMPANY", ["The provided company does not exist or is inactive."]);
        }

        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var statusRepository = _unitOfWork.GetRepository<TicketStatus>();
        var complexityRepository = _unitOfWork.GetRepository<TicketComplexity>();
        var timeUnitRepository = _unitOfWork.GetRepository<TimeUnit>();
        var channelRepository = _unitOfWork.GetRepository<CommunicationChannel>();
        var customerRepository = _unitOfWork.GetRepository<Person>();
        var projectRepository = _unitOfWork.GetRepository<Project>();
        var areaRepository = _unitOfWork.GetRepository<Area>();
        var userRepository = _unitOfWork.GetRepository<ApplicationUser>();

        var entity = await ticketRepository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId)
        {
            return Response<TicketDto>.Error("TICKET_NOT_FOUND", ["Ticket not found for the current company."]);
        }

        if (await statusRepository.GetAsync(request.TicketStatusId) is null)
        {
            return Response<TicketDto>.Error("INVALID_TICKET_STATUS", ["The provided ticket status does not exist or is inactive."]);
        }

        if (await complexityRepository.GetAsync(request.TicketComplexityId) is null)
        {
            return Response<TicketDto>.Error("INVALID_TICKET_COMPLEXITY", ["The provided ticket complexity does not exist or is inactive."]);
        }

        if (await timeUnitRepository.GetAsync(request.TimeUnitId) is null)
        {
            return Response<TicketDto>.Error("INVALID_TIME_UNIT", ["The provided time unit does not exist or is inactive."]);
        }

        if (await channelRepository.GetAsync(request.ChannelId) is null)
        {
            return Response<TicketDto>.Error("INVALID_CHANNEL", ["The provided communication channel does not exist or is inactive."]);
        }

        if (request.PersonId.HasValue && await customerRepository.GetAsync(request.PersonId.Value) is null)
        {
            return Response<TicketDto>.Error("INVALID_CUSTOMER", ["The provided customer does not exist for the current company."]);
        }

        if (request.ProjectId.HasValue && await projectRepository.GetAsync(request.ProjectId.Value) is null)
        {
            return Response<TicketDto>.Error("INVALID_PROJECT", ["The provided project does not exist for the current company."]);
        }

        if (request.AreaId.HasValue && await areaRepository.GetAsync(request.AreaId.Value) is null)
        {
            return Response<TicketDto>.Error("INVALID_AREA", ["The provided area does not exist for the current company."]);
        }

        if (request.PrecedentTicketId.HasValue)
        {
            if (request.PrecedentTicketId.Value == request.Id)
            {
                return Response<TicketDto>.Error("INVALID_PRECEDENT_TICKET", ["A ticket cannot reference itself as precedent."]);
            }

            var precedent = await ticketRepository.GetAsync(request.PrecedentTicketId.Value);
            if (precedent is null)
            {
                return Response<TicketDto>.Error("INVALID_PRECEDENT_TICKET", ["The precedent ticket does not exist for the current company."]);
            }
        }

        // Reassignment is no longer handled here — see SPEC 35 / ReassignTicketCommand.
        var previousStatusId = entity.TicketStatusId;
        var statusChanged = previousStatusId != request.TicketStatusId;

        _ticketMapper.ApplyUpdate(request, entity);
        entity.EffortPoints = request.EffortPoints;

        if (statusChanged)
        {
            entity.AddLog(
                currentUserId,
                LogType.StatusChange,
                "Estado actualizado",
                previousStatusId: previousStatusId);
        }

        await ticketRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketDto>.Error("UPDATE_FAILED", ["No records were affected while updating the ticket."]);
        }

        var dto = await _ticketDtoAssembler.BuildAsync(entity, company, cancellationToken);

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket updated successfully.",
            Data = dto
        };
    }
}
