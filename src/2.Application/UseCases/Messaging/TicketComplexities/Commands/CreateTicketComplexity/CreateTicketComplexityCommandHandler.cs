using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Commands;

/// <summary>
/// Handles ticket complexity creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateTicketComplexityCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<CreateTicketComplexityCommand, Response<TicketComplexityDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// Creates a ticket complexity catalog item.
    /// </summary>
    public async Task<Response<TicketComplexityDto>> Handle(CreateTicketComplexityCommand request, CancellationToken cancellationToken)
    {
        if (_currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketComplexityDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var ticketComplexityRepository = _unitOfWork.GetRepository<TicketComplexity>();
        var timeUnitRepository = _unitOfWork.GetRepository<TimeUnit>();

        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? null
            : request.Description.Trim();

        var existingTicketComplexities = await ticketComplexityRepository.GetAllAsync();
        var nameInUse = existingTicketComplexities.Any(tc =>
            tc.GcRecord == 0
            && string.Equals(tc.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<TicketComplexityDto>.Error("TICKET_COMPLEXITY_NAME_IN_USE", ["Another active ticket complexity already uses the same name."]);
        }

        var codeInUse = existingTicketComplexities.Any(tc =>
            tc.GcRecord == 0
            && tc.Code == request.Code);

        if (codeInUse)
        {
            return Response<TicketComplexityDto>.Error("TICKET_COMPLEXITY_CODE_IN_USE", ["Another active ticket complexity already uses the same code."]);
        }

        var timeUnit = await timeUnitRepository.GetAsync(request.TimeUnitId);
        if (timeUnit is null)
        {
            return Response<TicketComplexityDto>.Error("TIME_UNIT_NOT_FOUND", ["The related time unit was not found."]);
        }

        var entity = new TicketComplexity
        {
            CompanyId = _currentUserService.CompanyId,
            Name = normalizedName,
            Description = normalizedDescription,
            Code = request.Code,
            ResolutionTimeUnits = request.ResolutionTimeUnits,
            TimeUnitId = request.TimeUnitId,
            IsActive = request.IsActive
        };

        await ticketComplexityRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TicketComplexityDto>.Error("CREATE_FAILED", ["No records were affected while creating the ticket complexity."]);
        }

        return new Response<TicketComplexityDto>
        {
            IsSuccess = true,
            Message = "Ticket complexity created successfully.",
            Data = new TicketComplexityDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Code = entity.Code,
                ResolutionTimeUnits = entity.ResolutionTimeUnits,
                TimeUnitId = entity.TimeUnitId,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
