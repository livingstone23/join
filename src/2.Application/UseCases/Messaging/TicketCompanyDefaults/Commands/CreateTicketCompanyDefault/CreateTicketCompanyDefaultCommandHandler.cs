using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Commands;

/// <summary>
/// Handles tenant ticket default configuration creation.
/// </summary>
public sealed class CreateTicketCompanyDefaultCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ITicketCompanyDefaultMapper mapper)
    : IRequestHandler<CreateTicketCompanyDefaultCommand, Response<TicketCompanyDefaultDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ITicketCompanyDefaultMapper _mapper = mapper;

    /// <summary>
    /// Creates the tenant configuration when no active record exists for the current company.
    /// </summary>
    public async Task<Response<TicketCompanyDefaultDto>> Handle(CreateTicketCompanyDefaultCommand request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketCompanyDefaultDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = _unitOfWork.GetRepository<TicketCompanyDefault>();
        var existingConfigs = await repository.GetAllAsync();
        var activeExists = existingConfigs.Any(x => x.CompanyId == currentUserService.CompanyId && x.GcRecord == 0);

        if (activeExists)
        {
            return Response<TicketCompanyDefaultDto>.Error("CONFIG_ALREADY_EXISTS", ["An active ticket company default configuration already exists for the current tenant."]);
        }

        var (validationError, status, complexity, timeUnit, area, project, channel) = await ValidateReferencesAsync(request);
        if (validationError is not null)
        {
            return validationError;
        }

        var entity = _mapper.ToEntity(request);
        entity.CompanyId = currentUserService.CompanyId;
        entity.StartCode = request.StartCode.Trim();

        await repository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketCompanyDefaultDto>.Error("CREATE_FAILED", ["No records were affected while creating the configuration."]);
        }

        return new Response<TicketCompanyDefaultDto>
        {
            IsSuccess = true,
            Message = "Ticket company default configuration created successfully.",
            Data = BuildDto(entity, status?.Name, complexity?.Name, timeUnit?.Name, area?.Name, project?.Name, channel?.Name)
        };
    }

    private async Task<(Response<TicketCompanyDefaultDto>? Error, TicketStatus? Status, TicketComplexity? Complexity, TimeUnit? TimeUnit, Area? Area, Project? Project, CommunicationChannel? Channel)> ValidateReferencesAsync(CreateTicketCompanyDefaultCommand request)
    {
        var statusRepository = _unitOfWork.GetRepository<TicketStatus>();
        var complexityRepository = _unitOfWork.GetRepository<TicketComplexity>();
        var timeUnitRepository = _unitOfWork.GetRepository<TimeUnit>();
        var areaRepository = _unitOfWork.GetRepository<Area>();
        var projectRepository = _unitOfWork.GetRepository<Project>();
        var channelRepository = _unitOfWork.GetRepository<CommunicationChannel>();

        TicketStatus? status = null;
        if (request.TicketStatusDefaultId.HasValue)
        {
            status = await statusRepository.GetAsync(request.TicketStatusDefaultId.Value);
            if (status is null)
            {
                return (Response<TicketCompanyDefaultDto>.Error("INVALID_TICKET_STATUS", ["The provided default ticket status does not exist."]), null, null, null, null, null, null);
            }
        }

        TicketComplexity? complexity = null;
        if (request.TicketComplexityDefaultId.HasValue)
        {
            complexity = await complexityRepository.GetAsync(request.TicketComplexityDefaultId.Value);
            if (complexity is null)
            {
                return (Response<TicketCompanyDefaultDto>.Error("INVALID_TICKET_COMPLEXITY", ["The provided default ticket complexity does not exist."]), null, null, null, null, null, null);
            }
        }

        TimeUnit? timeUnit = null;
        if (request.TimeUnitDefaultId.HasValue)
        {
            timeUnit = await timeUnitRepository.GetAsync(request.TimeUnitDefaultId.Value);
            if (timeUnit is null)
            {
                return (Response<TicketCompanyDefaultDto>.Error("INVALID_TIME_UNIT", ["The provided default time unit does not exist."]), null, null, null, null, null, null);
            }
        }

        Area? area = null;
        if (request.AreaDefaultId.HasValue)
        {
            area = await areaRepository.GetAsync(request.AreaDefaultId.Value);
            if (area is null)
            {
                return (Response<TicketCompanyDefaultDto>.Error("INVALID_AREA", ["The provided default area does not exist for the current tenant."]), null, null, null, null, null, null);
            }
        }

        Project? project = null;
        if (request.ProjectDefaultId.HasValue)
        {
            project = await projectRepository.GetAsync(request.ProjectDefaultId.Value);
            if (project is null)
            {
                return (Response<TicketCompanyDefaultDto>.Error("INVALID_PROJECT", ["The provided default project does not exist for the current tenant."]), null, null, null, null, null, null);
            }
        }

        CommunicationChannel? channel = null;
        if (request.ChannelDefaultId.HasValue)
        {
            channel = await channelRepository.GetAsync(request.ChannelDefaultId.Value);
            if (channel is null)
            {
                return (Response<TicketCompanyDefaultDto>.Error("INVALID_CHANNEL", ["The provided default communication channel does not exist."]), null, null, null, null, null, null);
            }
        }

        return (null, status, complexity, timeUnit, area, project, channel);
    }

    private static TicketCompanyDefaultDto BuildDto(
        TicketCompanyDefault entity,
        string? statusName,
        string? complexityName,
        string? timeUnitName,
        string? areaName,
        string? projectName,
        string? channelName)
        => new()
        {
            Id = entity.Id,
            CompanyId = entity.CompanyId,
            StartCode = entity.StartCode,
            CodeSequenceLength = entity.CodeSequenceLength,
            UsePersonalizedCode = entity.UsePersonalizedCode,
            TicketStatusDefaultId = entity.TicketStatusDefaultId,
            StatusName = statusName,
            TicketComplexityDefaultId = entity.TicketComplexityDefaultId,
            ComplexityName = complexityName,
            TimeUnitDefaultId = entity.TimeUnitDefaultId,
            TimeUnitName = timeUnitName,
            AreaDefaultId = entity.AreaDefaultId,
            AreaName = areaName,
            ProjectDefaultId = entity.ProjectDefaultId,
            ProjectName = projectName,
            ChannelDefaultId = entity.ChannelDefaultId,
            ChannelName = channelName,
            CreatedAt = entity.Created
        };
}
