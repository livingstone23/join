using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.CreateUserNonWorkingPeriod;

/// <summary>
/// Handles <see cref="CreateUserNonWorkingPeriodCommand"/>: the user must belong to the company.
/// </summary>
public sealed class CreateUserNonWorkingPeriodCommandHandler(CalendarNonWorkingPeriodWriter writer)
    : IRequestHandler<CreateUserNonWorkingPeriodCommand, Response<CalendarNonWorkingPeriodResultDto>>
{
    public async Task<Response<CalendarNonWorkingPeriodResultDto>> Handle(CreateUserNonWorkingPeriodCommand request, CancellationToken cancellationToken)
    {
        var company = await writer.OpenAsync(cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(company.Message, company.Errors);
        }

        if (!await writer.IsCompanyMemberAsync(company.Data!.CompanyId, request.UserId))
        {
            return Response<CalendarNonWorkingPeriodResultDto>.Error(CalendarErrorCodes.UserNotInCompany, ["The user is not an active user of the current company."]);
        }

        return await writer.SaveAsync(company.Data, request.UserId, null, request.StartDate, request.EndDate, request.Reason, request.Notes, cancellationToken);
    }
}
