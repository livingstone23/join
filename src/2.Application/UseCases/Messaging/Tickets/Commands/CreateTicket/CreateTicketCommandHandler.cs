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
/// Handles ticket creation commands.
/// </summary>
public sealed class CreateTicketCommandHandler(
    IUnitOfWork unitOfWork,
    ITicketMapper ticketMapper,
    ICurrentUserService currentUserService,
    TicketDtoAssembler ticketDtoAssembler,
    TicketUserCompanyCapabilityResolver capabilityResolver,
    TicketCodeGenerator ticketCodeGenerator)
    : IRequestHandler<CreateTicketCommand, Response<TicketDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ITicketMapper _ticketMapper = ticketMapper;
    private readonly TicketDtoAssembler _ticketDtoAssembler = ticketDtoAssembler;
    private readonly TicketUserCompanyCapabilityResolver _capabilityResolver = capabilityResolver;
    private readonly TicketCodeGenerator _ticketCodeGenerator = ticketCodeGenerator;

    /// <summary>
    /// Creates a ticket for the current tenant and returns a flattened ticket projection.
    /// </summary>
    public async Task<Response<TicketDto>> Handle(CreateTicketCommand request, CancellationToken cancellationToken)
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

        var channel = await channelRepository.GetAsync(request.ChannelId);
        if (channel is null)
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

        if (request.AssignedToUserId.HasValue)
        {
            if (await userRepository.GetAsync(request.AssignedToUserId.Value) is null)
            {
                return Response<TicketDto>.Error("INVALID_ASSIGNED_USER", ["The assigned user does not exist or is inactive."]);
            }

            var assignedUserHasTenant = await _unitOfWork.UserCompanies
                .IsActiveMemberAsync(request.AssignedToUserId.Value, currentUserService.CompanyId, cancellationToken);

            if (!assignedUserHasTenant)
            {
                return Response<TicketDto>.Error("INVALID_ASSIGNED_USER_TENANT", ["The assigned user is not linked to the current company."]);
            }

            // SPEC 35 — only ticket-roster resolvers can receive an initial assignment.
            var capability = await _capabilityResolver.ResolveAsync(
                request.AssignedToUserId.Value,
                currentUserService.CompanyId,
                cancellationToken);

            if (!capability.CanResolveTicket)
            {
                return Response<TicketDto>.Error("INVALID_ASSIGNED_USER_NOT_RESOLVER", ["The assigned user does not have ticket-resolution capability for the current company."]);
            }
        }

        if (request.PrecedentTicketId.HasValue)
        {
            var precedent = await ticketRepository.GetAsync(request.PrecedentTicketId.Value);
            if (precedent is null)
            {
                return Response<TicketDto>.Error("INVALID_PRECEDENT_TICKET", ["The precedent ticket does not exist for the current company."]);
            }
        }

        var entity = _ticketMapper.ToEntity(request);
        entity.CompanyId = currentUserService.CompanyId;
        entity.CreatedByUserId = currentUserId;
        entity.EffortPoints = request.EffortPoints;

        // Per-company numbering driven by TicketCompanyDefaults (StartCode / CodeSequenceLength);
        // soft-deleted tickets count, so a code is never reissued.
        await _ticketCodeGenerator.AssignCodeAsync(entity, DateTime.UtcNow, cancellationToken);

        var creationSummary = string.IsNullOrWhiteSpace(channel.Name)
            ? "Ticket creado"
            : $"Ticket creado desde {channel.Name}";

        entity.AddLog(
            currentUserId,
            LogType.Creation,
            creationSummary);

        await ticketRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketDto>.Error("CREATE_FAILED", ["No records were affected while creating the ticket."]);
        }

        var dto = await _ticketDtoAssembler.BuildAsync(entity, company, cancellationToken);

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket created successfully.",
            Data = dto
        };
    }
}
