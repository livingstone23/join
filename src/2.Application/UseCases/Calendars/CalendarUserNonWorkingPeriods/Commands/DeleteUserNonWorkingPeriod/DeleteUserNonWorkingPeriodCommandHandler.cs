using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteUserNonWorkingPeriod;

/// <summary>
/// Handles <see cref="DeleteUserNonWorkingPeriodCommand"/>.
/// </summary>
public sealed class DeleteUserNonWorkingPeriodCommandHandler(CalendarNonWorkingPeriodWriter writer)
    : IRequestHandler<DeleteUserNonWorkingPeriodCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteUserNonWorkingPeriodCommand request, CancellationToken cancellationToken)
    {
        var company = await writer.OpenAsync(cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<bool>.Error(company.Message, company.Errors);
        }

        return await writer.DeleteAsync(company.Data!.CompanyId, request.Id, null, cancellationToken);
    }
}
