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
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateTicketCommand, Response<TicketDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ITicketMapper _ticketMapper = ticketMapper;

    /// <summary>
    /// Creates a ticket for the current tenant and returns a flattened ticket projection.
    /// </summary>
    public async Task<Response<TicketDto>> Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketDto>.Error("COMPANY_REQUIRED", ["The X-Company-Id header is required."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var currentUserId))
        {
            return Response<TicketDto>.Error("USER_REQUIRED", ["The authenticated user identifier is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        if (await companyRepository.GetAsync(currentUserService.CompanyId) is null)
        {
            return Response<TicketDto>.Error("INVALID_COMPANY", ["The provided company does not exist or is inactive."]);
        }

        var ticketRepository = _unitOfWork.GetRepository<Ticket>();
        var statusRepository = _unitOfWork.GetRepository<TicketStatus>();
        var complexityRepository = _unitOfWork.GetRepository<TicketComplexity>();
        var timeUnitRepository = _unitOfWork.GetRepository<TimeUnit>();
        var channelRepository = _unitOfWork.GetRepository<CommunicationChannel>();
        var customerRepository = _unitOfWork.GetRepository<Customer>();
        var projectRepository = _unitOfWork.GetRepository<Project>();
        var areaRepository = _unitOfWork.GetRepository<Area>();
        var userRepository = _unitOfWork.GetRepository<ApplicationUser>();
        var ticketCompanyDefaultRepository = _unitOfWork.GetRepository<TicketCompanyDefault>();

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

        if (request.CustomerId.HasValue && await customerRepository.GetAsync(request.CustomerId.Value) is null)
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

            var userCompanyRepository = _unitOfWork.GetRepository<UserCompany>();
            var userCompanies = await userCompanyRepository.GetAllAsync();
            var assignedUserHasTenant = userCompanies.Any(link =>
                link.GcRecord == 0
                && link.CompanyId == currentUserService.CompanyId
                && link.UserId == request.AssignedToUserId.Value);

            if (!assignedUserHasTenant)
            {
                return Response<TicketDto>.Error("INVALID_ASSIGNED_USER_TENANT", ["The assigned user is not linked to the current company."]);
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

        var existingTickets = await ticketRepository.GetAllAsync();
        var ticketCompanyDefaults = await ticketCompanyDefaultRepository.GetAllAsync();
        var ticketCompanyDefault = ticketCompanyDefaults.FirstOrDefault(x => x.CompanyId == currentUserService.CompanyId && x.GcRecord == 0);
        var now = DateTime.UtcNow;
        var monthlySequence = existingTickets.Count(ticket =>
                ticket.GcRecord == 0
                && ticket.CompanyId == currentUserService.CompanyId
                && ticket.Created.Year == now.Year
                && ticket.Created.Month == now.Month)
            + 1;

        var entity = _ticketMapper.ToEntity(request);
        entity.CompanyId = currentUserService.CompanyId;
        entity.CreatedByUserId = currentUserId;
        entity.EffortPoints = request.EffortPoints;

        if (ticketCompanyDefault is not null && ticketCompanyDefault.UsePersonalizedCode)
        {
            entity.SetPersonalizedCode(ticketCompanyDefault.StartCode, monthlySequence, ticketCompanyDefault.CodeSequenceLength);
        }
        else
        {
            entity.SetStandardCode(now.Year, now.Month, monthlySequence);
        }

        var duplicatedCode = existingTickets.Any(ticket =>
            ticket.GcRecord == 0
            && ticket.CompanyId == currentUserService.CompanyId
            && string.Equals(ticket.Code, entity.Code, StringComparison.OrdinalIgnoreCase));

        if (duplicatedCode)
        {
            return Response<TicketDto>.Error("TICKET_CODE_IN_USE", ["Generated ticket code is already in use. Try again."]);
        }

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

        var createdBy = await userRepository.GetAsync(entity.CreatedByUserId);
        var assignedTo = entity.AssignedToUserId.HasValue
            ? await userRepository.GetAsync(entity.AssignedToUserId.Value)
            : null;
        var status = await statusRepository.GetAsync(entity.TicketStatusId);
        var complexity = await complexityRepository.GetAsync(entity.TicketComplexityId);
        var timeUnit = await timeUnitRepository.GetAsync(entity.TimeUnitId);
        var customer = entity.CustomerId.HasValue ? await customerRepository.GetAsync(entity.CustomerId.Value) : null;
        var project = entity.ProjectId.HasValue ? await projectRepository.GetAsync(entity.ProjectId.Value) : null;
        var area = entity.AreaId.HasValue ? await areaRepository.GetAsync(entity.AreaId.Value) : null;
        var precedentTicket = entity.PrecedentTicketId.HasValue ? await ticketRepository.GetAsync(entity.PrecedentTicketId.Value) : null;

        return new Response<TicketDto>
        {
            IsSuccess = true,
            Message = "Ticket created successfully.",
            Data = new TicketDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                Code = entity.Code,
                Name = entity.Name,
                Description = entity.Description,
                EstimatedTime = entity.EstimatedTime,
                ConsumedTime = entity.ConsumedTime,
                EffortPoints = entity.EffortPoints,
                IsVisibleToExternals = entity.IsVisibleToExternals,
                TicketStatusId = entity.TicketStatusId,
                TicketStatusName = status?.Name ?? string.Empty,
                TicketComplexityId = entity.TicketComplexityId,
                TicketComplexityName = complexity?.Name ?? string.Empty,
                TimeUnitId = entity.TimeUnitId,
                TimeUnitName = timeUnit?.Name ?? string.Empty,
                CustomerId = entity.CustomerId,
                CustomerName = ResolveCustomerName(customer),
                ProjectId = entity.ProjectId,
                ProjectName = project?.Name,
                AreaId = entity.AreaId,
                AreaName = area?.Name,
                ChannelId = entity.ChannelId,
                ChannelName = channel?.Name ?? string.Empty,
                CreatedByUserId = entity.CreatedByUserId,
                CreatedByUserName = ResolveUserName(createdBy),
                AssignedToUserId = entity.AssignedToUserId,
                AssignedToUserName = ResolveUserName(assignedTo),
                PrecedentTicketId = entity.PrecedentTicketId,
                PrecedentTicketCode = precedentTicket?.Code,
                CreatedAt = entity.Created
            }
        };
    }

    private static string? ResolveCustomerName(Customer? customer)
    {
        if (customer is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(customer.CommercialName))
        {
            return customer.CommercialName;
        }

        return string.Join(" ", new[] { customer.FirstName, customer.MiddleName, customer.LastName, customer.SecondLastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string? ResolveUserName(ApplicationUser? user)
    {
        if (user is null)
        {
            return null;
        }

        return string.Join(" ", new[] { user.FirstName, user.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }
}
