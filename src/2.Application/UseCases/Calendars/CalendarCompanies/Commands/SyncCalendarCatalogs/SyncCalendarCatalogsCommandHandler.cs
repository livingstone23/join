using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Commands.SyncCalendarCatalogs;

/// <summary>
/// Handles <see cref="SyncCalendarCatalogsCommand"/> through <see cref="ICompanyCatalogSeeder.SeedCalendarCatalogsForCompanyAsync"/>.
/// </summary>
public sealed class SyncCalendarCatalogsCommandHandler(ICompanyCatalogSeeder catalogSeeder, ICurrentUserService currentUserService)
    : IRequestHandler<SyncCalendarCatalogsCommand, Response<CalendarCatalogSyncDto>>
{
    public async Task<Response<CalendarCatalogSyncDto>> Handle(SyncCalendarCatalogsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarCatalogSyncDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var result = await catalogSeeder.SeedCalendarCatalogsForCompanyAsync(currentUserService.CompanyId, cancellationToken);
        return new Response<CalendarCatalogSyncDto>
        {
            IsSuccess = true,
            Message = "Calendar catalogs synchronized successfully.",
            Data = new CalendarCatalogSyncDto(result.ActivitiesAdded, result.StatusesAdded)
        };
    }
}
