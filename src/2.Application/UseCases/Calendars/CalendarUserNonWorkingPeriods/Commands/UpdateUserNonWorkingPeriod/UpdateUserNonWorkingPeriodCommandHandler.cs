using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.UpdateUserNonWorkingPeriod;

/// <summary>
/// Handles <see cref="UpdateUserNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class UpdateUserNonWorkingPeriodCommandHandler(CalendarNonWorkingPeriodWriter writer)
    : IRequestHandler<UpdateUserNonWorkingPeriodCommand, Response<CalendarNonWorkingPeriodResultDto>>
{
    public async Task<Response<CalendarNonWorkingPeriodResultDto>> Handle(UpdateUserNonWorkingPeriodCommand request, CancellationToken cancellationToken)
    {
        var company = await writer.OpenAsync(cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(company.Message, company.Errors);
        }

        var entity = await writer.FindAsync(company.Data!.CompanyId, request.Id);
        if (entity is null)
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(CalendarErrorCodes.NonWorkingPeriodNotFound, ["The non-working period was not found."]);
        }

        return await writer.SaveAsync(company.Data, entity.UserId, entity, request.StartDate, request.EndDate, request.Reason, request.Notes, cancellationToken);
    }
}
