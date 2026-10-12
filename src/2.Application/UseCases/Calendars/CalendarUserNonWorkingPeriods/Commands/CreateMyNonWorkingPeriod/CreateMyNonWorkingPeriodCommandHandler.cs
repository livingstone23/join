using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateMyNonWorkingPeriod;

/// <summary>
/// Handles <see cref="CreateMyNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class CreateMyNonWorkingPeriodCommandHandler(CalendarNonWorkingPeriodWriter writer)
    : IRequestHandler<CreateMyNonWorkingPeriodCommand, Response<CalendarNonWorkingPeriodResultDto>>
{
    public async Task<Response<CalendarNonWorkingPeriodResultDto>> Handle(CreateMyNonWorkingPeriodCommand request, CancellationToken cancellationToken)
    {
        var company = await writer.OpenAsync(cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(company.Message, company.Errors);
        }

        if (writer.CurrentUserId is not { } userId)
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        return await writer.SaveAsync(company.Data!, userId, null, request.StartDate, request.EndDate, request.Reason, request.Notes, cancellationToken);
    }
}
