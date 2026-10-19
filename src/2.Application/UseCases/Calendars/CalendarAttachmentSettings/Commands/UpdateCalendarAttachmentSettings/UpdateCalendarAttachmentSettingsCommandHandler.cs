using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarAttachmentSettings.Commands;

/// <summary>
/// SPEC 69 — handles <see cref="UpdateCalendarAttachmentSettingsCommand"/>: loads the unique live row of the
/// current tenant, replaces the fields and saves. Fails with the same not-configured error as the GET when
/// the row does not exist; the UI creates it via <c>POST</c> in that case.
/// </summary>
public sealed class UpdateCalendarAttachmentSettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateCalendarAttachmentSettingsCommand, Response<CalendarAttachmentSettingsDto>>
{
    public async Task<Response<CalendarAttachmentSettingsDto>> Handle(
        UpdateCalendarAttachmentSettingsCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarAttachmentSettingsDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<JOIN.Domain.Calendars.CalendarAttachmentSettings>();
        var entity = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();

        if (entity is null)
        {
            return Response<CalendarAttachmentSettingsDto>.Error(
                CalendarErrorCodes.AttachmentSettingsNotConfigured,
                ["The company has no calendar attachment settings row yet; create it first."]);
        }

        entity.AllowedDocumentTypes = request.AllowedDocumentTypes;
        entity.MaxFileSizeBytes = request.MaxFileSizeBytes;
        entity.MaxFilesPerComment = request.MaxFilesPerComment;
        entity.MaxFilesPerDay = request.MaxFilesPerDay;
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarAttachmentSettingsDto>
        {
            IsSuccess = true,
            Message = "Calendar attachment settings updated successfully.",
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