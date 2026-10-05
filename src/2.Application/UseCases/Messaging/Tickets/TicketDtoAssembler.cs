using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Security;
using JOIN.Domain.Support;

namespace JOIN.Application.UseCases.Messaging.Tickets;

/// <summary>
/// Consolidates the <c>Ticket</c> entity → <see cref="TicketDto"/> projection that
/// was previously duplicated between <c>CreateTicketCommandHandler</c> and
/// <c>UpdateTicketCommandHandler</c>. Resolves every related entity (status,
/// complexity, time unit, customer, project, area, channel, creator, assignee,
/// precedent ticket) by id and composes the DTO.
/// Also computes SLA / inactivity via <see cref="TicketSlaCalculator"/> so every write
/// path (create, update, reassign, finish) returns a DTO identical to the read queries
/// (SPEC 37 F6).
/// </summary>
public sealed class TicketDtoAssembler(IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Builds a <see cref="TicketDto"/> from the given entity. The <paramref name="company"/>
    /// is supplied by the caller because it is normally loaded up-front to validate tenant membership;
    /// every other relation is fetched here so handlers do not duplicate the lookup block.
    /// </summary>
    public async Task<TicketDto> BuildAsync(Ticket entity, Company company, CancellationToken cancellationToken)
    {
        var userRepository = unitOfWork.GetRepository<ApplicationUser>();
        var statusRepository = unitOfWork.GetRepository<TicketStatus>();
        var complexityRepository = unitOfWork.GetRepository<TicketComplexity>();
        var timeUnitRepository = unitOfWork.GetRepository<TimeUnit>();
        var customerRepository = unitOfWork.GetRepository<Person>();
        var projectRepository = unitOfWork.GetRepository<Project>();
        var areaRepository = unitOfWork.GetRepository<Area>();
        var channelRepository = unitOfWork.GetRepository<CommunicationChannel>();
        var ticketRepository = unitOfWork.GetRepository<Ticket>();
        var ticketLogRepository = unitOfWork.GetRepository<TicketLog>();
        var ticketCompanyDefaultRepository = unitOfWork.GetRepository<TicketCompanyDefault>();

        var createdBy = await userRepository.GetAsync(entity.CreatedByUserId);
        var assignedTo = entity.AssignedToUserId.HasValue
            ? await userRepository.GetAsync(entity.AssignedToUserId.Value)
            : null;
        var status = await statusRepository.GetAsync(entity.TicketStatusId);
        var complexity = await complexityRepository.GetAsync(entity.TicketComplexityId);
        var timeUnit = await timeUnitRepository.GetAsync(entity.TimeUnitId);
        var customer = entity.PersonId.HasValue ? await customerRepository.GetAsync(entity.PersonId.Value) : null;
        var project = entity.ProjectId.HasValue ? await projectRepository.GetAsync(entity.ProjectId.Value) : null;
        var area = entity.AreaId.HasValue ? await areaRepository.GetAsync(entity.AreaId.Value) : null;
        var channel = await channelRepository.GetAsync(entity.ChannelId);
        var precedentTicket = entity.PrecedentTicketId.HasValue
            ? await ticketRepository.GetAsync(entity.PrecedentTicketId.Value)
            : null;

        // --- SLA / inactivity inputs (SPEC 37 F6) ---
        var ticketCompanyDefaults = await ticketCompanyDefaultRepository.GetAllAsync();
        var maxDayTicketInactivity = ticketCompanyDefaults
            .Where(x => x.GcRecord == 0 && x.CompanyId == entity.CompanyId)
            .Select(x => (int?)x.MaxDayTicketInactivity)
            .FirstOrDefault();

        // CRITICAL: the SLA multiplier comes from the TimeUnit pointed at by the
        // complexity (TicketComplexity.TimeUnitId), not from the ticket's own
        // TimeUnitId. Reusing the ticket's unit would silently mis-scale SLA by
        // a factor of 24 when the two differ.
        var complexityTimeUnit = complexity is not null
            ? await timeUnitRepository.GetAsync(complexity.TimeUnitId)
            : null;
        var complexityTimeUnitCode = complexityTimeUnit?.Code ?? 0;

        var ticketLogs = await ticketLogRepository.GetAllAsync();
        var logsForTicket = ticketLogs
            .Where(x => x.GcRecord == 0 && x.TicketId == entity.Id)
            .ToList();

        var lastActivityAt = logsForTicket.Count > 0
            ? logsForTicket.Max(x => x.Created)
            : entity.Created;

        var finishedAt = logsForTicket
            .Where(x => (int)x.LogType == (int)LogType.Finalization)
            .Select(x => (DateTime?)x.Created)
            .DefaultIfEmpty(null)
            .Max();

        var sla = TicketSlaCalculator.Compute(
            entity.Created,
            complexity?.ResolutionTimeUnits ?? 0,
            complexityTimeUnitCode,
            lastActivityAt,
            finishedAt,
            status?.IsFinal ?? false,
            maxDayTicketInactivity,
            nowUtc: DateTime.UtcNow);

        return new TicketDto
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            CompanyName = company.Name,
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
            PersonId = entity.PersonId,
            PersonName = ResolvePersonName(customer),
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
            CreatedAt = entity.Created,
            SlaDueAt = sla.SlaDueAt,
            IsSlaBreached = sla.IsSlaBreached,
            LastActivityAt = sla.LastActivityAt,
            IsInactive = sla.IsInactive
        };
    }

    private static string? ResolvePersonName(Person? customer)
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
