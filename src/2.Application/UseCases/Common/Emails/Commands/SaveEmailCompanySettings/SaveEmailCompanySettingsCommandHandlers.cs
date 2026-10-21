using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using MediatR;
using Settings = JOIN.Domain.Admin.EmailCompanySettings;

namespace JOIN.Application.UseCases.Common.Emails.Commands;

/// <summary>Handles <see cref="CreateEmailCompanySettingsCommand"/>: one live row per company.</summary>
public sealed class CreateEmailCompanySettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateEmailCompanySettingsCommand, Response<EmailCompanySettingsDto>>
{
    public async Task<Response<EmailCompanySettingsDto>> Handle(CreateEmailCompanySettingsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailCompanySettingsRules.CompanyRequired<EmailCompanySettingsDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        if ((await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).Any())
        {
            return Response<EmailCompanySettingsDto>.Error(
                PersonEmailErrorCodes.EmailSettingsAlreadyExists,
                ["An active email settings row already exists for this company."]);
        }

        var entity = new Settings { CompanyId = companyId };
        EmailCompanySettingsRules.Copy(entity, request);
        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<EmailCompanySettingsDto>
        {
            IsSuccess = true,
            Message = "Email settings created successfully.",
            Data = EmailCompanySettingsRules.ToDto(entity)
        };
    }
}

/// <summary>Handles <see cref="UpdateEmailCompanySettingsCommand"/>.</summary>
public sealed class UpdateEmailCompanySettingsCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateEmailCompanySettingsCommand, Response<EmailCompanySettingsDto>>
{
    public async Task<Response<EmailCompanySettingsDto>> Handle(UpdateEmailCompanySettingsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return EmailCompanySettingsRules.CompanyRequired<EmailCompanySettingsDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        var current = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).FirstOrDefault();
        if (current is null)
        {
            return EmailCompanySettingsRules.NotConfigured<EmailCompanySettingsDto>();
        }

        // Tracked re-read: GetAll* is untracked.
        var entity = (await repository.GetIncludingDeletedAsync(current.Id))!;
        EmailCompanySettingsRules.Copy(entity, request);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<EmailCompanySettingsDto>
        {
            IsSuccess = true,
            Message = "Email settings updated successfully.",
            Data = EmailCompanySettingsRules.ToDto(entity)
        };
    }
}

/// <summary>Mapping and errors shared by the email settings handlers.</summary>
internal static class EmailCompanySettingsRules
{
    internal static void Copy(Settings entity, IEmailCompanySettingsInput input)
    {
        entity.FromEmail = string.IsNullOrWhiteSpace(input.FromEmail) ? null : input.FromEmail.Trim();
        entity.FromName = string.IsNullOrWhiteSpace(input.FromName) ? null : input.FromName.Trim();
        entity.MaxEmailsPerUserPerDay = input.MaxEmailsPerUserPerDay;
        entity.MaxAttachmentsTotalBytes = input.MaxAttachmentsTotalBytes;
    }

    internal static EmailCompanySettingsDto ToDto(Settings s) => new()
    {
        Id = s.Id,
        CompanyId = s.CompanyId,
        FromEmail = s.FromEmail,
        FromName = s.FromName,
        MaxEmailsPerUserPerDay = s.MaxEmailsPerUserPerDay,
        MaxAttachmentsTotalBytes = s.MaxAttachmentsTotalBytes
    };

    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(PersonEmailErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<T> NotConfigured<T>() =>
        Response<T>.Error(PersonEmailErrorCodes.EmailSettingsNotConfigured, ["The company has no email settings row yet."]);
}
