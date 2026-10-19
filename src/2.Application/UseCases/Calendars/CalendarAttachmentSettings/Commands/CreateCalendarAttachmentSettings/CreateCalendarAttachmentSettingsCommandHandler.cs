using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;

/// <summary>
/// SPEC 69 — handles <see cref="CreateCalendarAttachmentSettingsCommand"/>. The unique live row per tenant
/// (filter <c>GcRecord = 0</c>) is enforced in memory: a duplicate is answered with
/// <c>CALENDAR_ATTACHMENT_SETTINGS_ALREADY_EXISTS</c> so the UI can switch to <c>PUT</c>.
/// </summary>
public sealed class CreateCalendarAttachmentSettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateCalendarAttachmentSettingsCommand, Response<CalendarAttachmentSettingsDto>>
{
    public async Task<Response<CalendarAttachmentSettingsDto>> Handle(
        CreateCalendarAttachmentSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarAttachmentSettingsDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<JOIN.Domain.Calendars.CalendarAttachmentSettings>();
        var activeExists = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).Any();
        if (activeExists)
        {
            return Response<CalendarAttachmentSettingsDto>.Error(
                "CALENDAR_ATTACHMENT_SETTINGS_ALREADY_EXISTS",
                ["An active calendar attachment settings row already exists for this company."]);
        }

        var entity = new JOIN.Domain.Calendars.CalendarAttachmentSettings
        {
            CompanyId = companyId,
            AllowedDocumentTypes = request.AllowedDocumentTypes,
            MaxFileSizeBytes = request.MaxFileSizeBytes,
            MaxFilesPerComment = request.MaxFilesPerComment,
            MaxFilesPerDay = request.MaxFilesPerDay
        };

        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Calendar attachment settings created successfully.",
            Data = new CalendarAttachmentSettingsDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                AllowedDocumentTypes = (int)entity.AllowedDocumentTypes,
                MaxFileSizeBytes = entity.MaxFileSizeBytes,
                MaxFilesPerComment = entity.MaxFilesPerComment,
                MaxFilesPerDay = entity.MaxFilesPerDay,
                CreatedAt = entity.Created
            }
        };
    }
}