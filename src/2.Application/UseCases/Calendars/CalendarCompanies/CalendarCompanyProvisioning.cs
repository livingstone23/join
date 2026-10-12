using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Security;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies;

/// <summary>
/// Provisions the default calendar of every active user of the company that has none (SPEC 44, decision 20),
/// through <see cref="CalendarUserProvisioner"/>. Used by <c>POST /CalendarCompanies</c> and <c>provision-users</c>.
/// </summary>
public sealed class CalendarCompanyProvisioning(IUnitOfWork unitOfWork, CalendarUserProvisioner provisioner)
{
    public async Task<IReadOnlyList<CalendarProvisionedUserDto>> ProvisionPendingUsersAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var memberIds = (await unitOfWork.GetRepository<UserCompany>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .Select(x => x.UserId)
            .Distinct()
            .ToList();

        var provisionedIds = (await unitOfWork.GetRepository<CalendarUserConfiguration>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .Select(x => x.UserId)
            .ToHashSet();

        var pending = memberIds.Where(id => !provisionedIds.Contains(id)).ToList();
        if (pending.Count == 0)
        {
            return [];
        }

        var users = (await unitOfWork.GetRepository<ApplicationUser>()
                .GetAllIncludingDeletedAsync(u => pending.Contains(u.Id) && u.GcRecord == 0 && u.IsActive))
            .OrderBy(u => u.FirstName)
            .ToList();

        var result = new List<CalendarProvisionedUserDto>();
        foreach (var user in users)
        {
            if (await provisioner.EnsureUserCalendarAsync(companyId, user.Id, cancellationToken))
            {
                result.Add(new CalendarProvisionedUserDto(user.Id, $"{user.FirstName} {user.LastName}".Trim(), user.Email));
            }
        }

        return result;
    }
}
