using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Queries;

/// <summary>
/// SPEC 69 — handles <see cref="GetCalendarAttachmentSettingsQuery"/> by loading the unique live row of the
/// current tenant (filter on <c>GcRecord = 0</c>). A missing row yields the 400 error code so the UI can
/// show the configuration tab; the seed creates the row on every new company.
/// </summary>
public sealed class GetCalendarAttachmentSettingsQueryHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarAttachmentSettingsQuery, Response<CalendarAttachmentSettingsDto>>
{
    public async Task<Response<CalendarAttachmentSettingsDto>> Handle(
        GetCalendarAttachmentSettingsQuery request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarAttachmentSettingsDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var settings = (await unitOfWork.GetRepository<JOIN.Domain.Calendars.CalendarAttachmentSettings>()
            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();

        if (settings is null)
        {
            return Response<CalendarAttachmentSettingsDto>.Error(
                CalendarErrorCodes.AttachmentSettingsNotConfigured,
                ["The company has no calendar attachment settings row yet."]);
        }

        return new Response<CalendarAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Calendar attachment settings retrieved successfully.",
            Data = new CalendarAttachmentSettingsDto
            {
                Id = settings.Id,
                CompanyId = settings.CompanyId,
                AllowedDocumentTypes = (int)settings.AllowedDocumentTypes,
                MaxFileSizeBytes = settings.MaxFileSizeBytes,
                MaxFilesPerComment = settings.MaxFilesPerComment,
                MaxFilesPerDay = settings.MaxFilesPerDay,
                CreatedAt = settings.Created
            }
        };
    }
}