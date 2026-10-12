using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.ProvisionCalendarUsers;

/// <summary>
/// Handles <see cref="ProvisionCalendarUsersCommand"/>. Requires the calendar configuration (R1).
/// </summary>
public sealed class ProvisionCalendarUsersCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarCompanyProvisioning provisioning)
    : IRequestHandler<ProvisionCalendarUsersCommand, Response<IReadOnlyList<CalendarProvisionedUserDto>>>
{
    public async Task<Response<IReadOnlyList<CalendarProvisionedUserDto>>> Handle(ProvisionCalendarUsersCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CalendarProvisionedUserDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var company = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<IReadOnlyList<CalendarProvisionedUserDto>>.Error(company.Message, company.Errors);
        }

        var provisioned = await provisioning.ProvisionPendingUsersAsync(companyId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<IReadOnlyList<CalendarProvisionedUserDto>>
        {
            IsSuccess = true,
            Data = provisioned,
            Message = $"Users provisioned: {provisioned.Count}."
        };
    }
}
