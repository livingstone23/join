using JOIN.Application.Common;
using JOIN.Application.Interface.Email;
using JOIN.Application.Interface.Persistence;
using Template = JOIN.Domain.Admin.EmailTemplate;
using TemplateType = JOIN.Domain.Admin.EmailTemplateType;

namespace JOIN.Application.UseCases.Common.EmailTemplates.Commands;

/// <summary>
/// Shared checks of the create and update commands (SPEC 71): the type must be an active type of the
/// company, the name must be free in the company, the subject and the body may only use the closed list
/// of variables and the body is stored sanitized.
/// </summary>
public sealed class EmailTemplateWriteGuard(
    IUnitOfWork unitOfWork,
    IEmailTemplateRenderer renderer,
    IHtmlSanitizer sanitizer)
{
    /// <summary>
    /// The live type of the company; inactive types are rejected unless <paramref name="allowInactive"/> (the
    /// template already had it).
    /// </summary>
    internal async Task<(TemplateType? Type, Response<string>? Error)> ResolveTypeAsync(
        Guid companyId, Guid typeId, CancellationToken cancellationToken, bool allowInactive = false)
    {
        var type = await unitOfWork.GetRepository<TemplateType>().GetAsync(typeId);
        if (type is null || type.CompanyId != companyId || type.GcRecord != 0 || (!type.IsActive && !allowInactive))
        {
            return (null, EmailTemplateErrors.TypeNotFound<string>());
        }

        return (type, null);
    }

    internal async Task<Response<string>?> ValidateAsync(
        Guid companyId,
        Guid? exceptTemplateId,
        string name,
        string subject,
        string bodyHtml,
        CancellationToken cancellationToken)
    {
        var trimmedName = name.Trim();
        var duplicate = (await unitOfWork.GetRepository<Template>().GetAllIncludingDeletedAsync(
            x => x.CompanyId == companyId
                && x.Name == trimmedName
                && x.GcRecord == 0
                && (!exceptTemplateId.HasValue || x.Id != exceptTemplateId.Value)))
            .Any();
        if (duplicate)
        {
            return EmailTemplateErrors.DuplicateName<string>();
        }

        var unknown = renderer.Validate(subject, bodyHtml);
        if (unknown.Count > 0)
        {
            return EmailTemplateErrors.UnknownVariables<string>(unknown);
        }

        return null;
    }

    internal string Sanitize(string bodyHtml) => sanitizer.Sanitize(bodyHtml);
}
