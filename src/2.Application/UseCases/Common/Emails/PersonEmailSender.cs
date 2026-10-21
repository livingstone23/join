using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Email;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Email;
using JOIN.Application.UseCases.Common.EmailTemplates;
using JOIN.Application.UseCases.Customers.Leads.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Catalog;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using JOIN.Domain.Security;
using Microsoft.Extensions.Logging;
using LeadContactType = JOIN.Domain.Customers.Leads.ContactType;
using PersonContactType = JOIN.Domain.Enums.ContactType;

namespace JOIN.Application.UseCases.Common.Emails;

/// <summary>An attachment asked by the client: a live document of a product (1) or of the associated lead (2).</summary>
public sealed record PersonEmailAttachmentInput
{
    /// <summary>1 ProductDocument, 2 LeadDocument.</summary>
    public int SourceKind { get; init; }
    public Guid SourceId { get; init; }
}

/// <summary>What the three send commands hand to <see cref="PersonEmailSender"/> (SPEC 72 B.7).</summary>
public sealed record PersonEmailSendRequest(
    Guid PersonId,
    PersonEmailOrigin Origin,
    Guid? CustomerId,
    Guid? LeadId,
    string? ToAddress,
    IReadOnlyList<string> Cc,
    string Subject,
    string BodyHtml,
    Guid? EmailTemplateId,
    IReadOnlyList<PersonEmailAttachmentInput> Attachments);

/// <summary>
/// SPEC 72 B.7 — the sending shared by the person, customer and lead commands. In order: the valid destination (rule 1),
/// the lead (rule 3) and the daily quota (rule 9), the attachments with their total size (rule 7), the sanitized and
/// rendered content and the sender (rules 6 and 8), the delivery, and one save of the message (<c>Sent</c> or
/// <c>Failed</c>), its attachments and, when it was sent and has a lead, the lead contact (rule 4). The delivery runs
/// outside any transaction: the commands do not implement <c>ITransactionalCommand</c>.
/// </summary>
public sealed class PersonEmailSender(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    LeadCommandContext leadContext,
    IFileStorageResolver fileStorageResolver,
    IHtmlSanitizer sanitizer,
    IEmailTemplateRenderer renderer,
    EmailTemplateContextReader contextReader,
    IEmailService emailService,
    IEmailSenderDefaults senderDefaults,
    ILogger<PersonEmailSender> logger)
{
    public const int MaxAttachments = 10;
    public const int MaxCc = 5;
    public const int SummaryMaxLength = 2000;
    public const string EmailContactTypeCode = "EMAIL";

    public async Task<Response<PersonEmailSentDto>> SendAsync(PersonEmailSendRequest request, CancellationToken cancellationToken)
    {
        if (LeadCommandContext.ActorOf<PersonEmailSentDto>(currentUserService, out var actor) is { } noActor)
        {
            return noActor;
        }

        // 1. The person and its valid destination (rules 1 and 10).
        var person = (await unitOfWork.GetRepository<Person>().GetAllIncludingDeletedAsync(
            x => x.Id == request.PersonId && x.CompanyId == actor.CompanyId && x.GcRecord == 0)).FirstOrDefault();
        if (person is null)
        {
            return Error(PersonEmailErrorCodes.PersonNotFound, "Person not found.");
        }

        if (!person.IsActive)
        {
            return Error(PersonEmailErrorCodes.PersonNotActive, "The person is inactive and does not receive emails.");
        }

        var addresses = await PersonEmailAddresses.ValidAsync(unitOfWork, actor.CompanyId, person.Id);
        if (addresses.Count == 0)
        {
            return Error(PersonEmailErrorCodes.PersonEmailNotAvailable, "The person has no valid email address.");
        }

        var to = string.IsNullOrWhiteSpace(request.ToAddress)
            ? addresses[0].Value
            : addresses.FirstOrDefault(a => string.Equals(a.Value, request.ToAddress.Trim(), StringComparison.OrdinalIgnoreCase))?.Value;
        if (to is null)
        {
            return Error(PersonEmailErrorCodes.InvalidEmailAddress, "The destination must be one of the valid email addresses of the person.");
        }

        var cc = request.Cc.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (cc.Count > MaxCc || cc.Any(c => !EmailAddressValidator.IsValid(c)))
        {
            return Error(PersonEmailErrorCodes.InvalidEmailAddress, $"Up to {MaxCc} copies with a valid email format are allowed.");
        }

        // 2. The template, the lead and the daily quota (rules 3, 6 and 9).
        if (request.EmailTemplateId is { } templateId)
        {
            var template = await unitOfWork.GetRepository<EmailTemplate>().GetAsync(templateId);
            if (template is null || template.CompanyId != actor.CompanyId || template.GcRecord != 0 || !template.IsActive)
            {
                return Error(PersonEmailErrorCodes.EmailTemplateNotFound, "Email template not found or inactive.");
            }
        }

        Lead? lead = null;
        Guid? emailContactTypeId = null;
        if (request.LeadId is { } leadId && leadId != Guid.Empty)
        {
            var (load, leadError) = await leadContext.LoadAsync<PersonEmailSentDto>(actor, leadId, cancellationToken);
            if (leadError is not null)
            {
                return leadError;
            }

            lead = load!.Lead;
            if (lead.PersonId != person.Id)
            {
                return Error(PersonEmailErrorCodes.InvalidLeadPerson, "The lead belongs to another person.");
            }

            if (LeadCommandContext.RequireOpen<PersonEmailSentDto>(lead) is { } notOpen)
            {
                return notOpen;
            }

            emailContactTypeId = await EmailContactTypeAsync(actor.CompanyId);
            if (emailContactTypeId is null)
            {
                return Error(PersonEmailErrorCodes.EmailContactTypeNotConfigured, "The company has no active contact type with code EMAIL.");
            }
        }

        var settings = (await unitOfWork.GetRepository<EmailCompanySettings>().GetAllIncludingDeletedAsync(
            x => x.CompanyId == actor.CompanyId && x.GcRecord == 0)).FirstOrDefault();
        if (settings is null)
        {
            return Error(PersonEmailErrorCodes.EmailSettingsNotConfigured, "The company has no email settings row yet.");
        }

        var startOfDay = DateTime.UtcNow.Date;
        var sentToday = (await unitOfWork.GetRepository<PersonEmailMessage>().GetAllIncludingDeletedAsync(
            x => x.CompanyId == actor.CompanyId && x.SentByUserId == actor.UserId && x.SentAt >= startOfDay && x.GcRecord == 0)).Count();
        if (sentToday >= settings.MaxEmailsPerUserPerDay)
        {
            return Error(PersonEmailErrorCodes.EmailDailyLimitReached, $"The daily limit of {settings.MaxEmailsPerUserPerDay} emails per user was reached.");
        }

        // 3. The attachments, from the provider that stored each document (rule 7).
        var (attachments, attachmentError) = await LoadAttachmentsAsync(request.Attachments, actor.CompanyId, lead, settings.MaxAttachmentsTotalBytes, cancellationToken);
        if (attachmentError is not null)
        {
            return attachmentError;
        }

        // 4. Sanitized and rendered content, sender and Reply-To (rules 6 and 8).
        var body = sanitizer.Sanitize(request.BodyHtml);
        var unknown = renderer.Validate(request.Subject, body);
        if (unknown.Count > 0)
        {
            return Error(PersonEmailErrorCodes.EmailTemplateUnknownVariable, $"Unknown template variables: {string.Join(", ", unknown)}.");
        }

        var context = await contextReader.ReadAsync(actor.CompanyId, actor.UserId, person.Id, to, lead?.Id, cancellationToken);
        if (!context.IsSuccess)
        {
            return Response<PersonEmailSentDto>.Error(context.Message, context.Errors);
        }

        var rendered = renderer.Render(request.Subject.Trim(), body, context.Data!);
        var fromEmail = settings.FromEmail ?? senderDefaults.FromEmail;
        var fromName = settings.FromEmail is null ? senderDefaults.FromName : settings.FromName;
        var replyTo = (await unitOfWork.GetRepository<ApplicationUser>().GetIncludingDeletedAsync(actor.UserId))?.Email;

        // 5. The delivery.
        var sentAt = DateTime.UtcNow;
        var result = await emailService.SendAsync(new EmailMessage
        {
            To = to,
            Cc = cc,
            FromEmail = fromEmail,
            FromName = fromName,
            ReplyTo = replyTo,
            Subject = rendered.Subject,
            HtmlBody = rendered.BodyHtml,
            Attachments = attachments.Select(a => new EmailAttachment(a.FileName, a.ContentType, a.Content)).ToList()
        }, cancellationToken);

        // 6. One save: the message, its attachments and, when it was sent and has a lead, the contact of the lead.
        var message = new PersonEmailMessage
        {
            CompanyId = actor.CompanyId,
            PersonId = person.Id,
            CustomerId = request.CustomerId,
            LeadId = lead?.Id,
            EmailTemplateId = request.EmailTemplateId,
            Origin = request.Origin,
            ToAddress = to,
            CcAddresses = cc.Count == 0 ? null : string.Join(",", cc),
            FromAddress = fromEmail ?? string.Empty,
            ReplyToAddress = replyTo,
            Subject = rendered.Subject,
            BodyHtml = rendered.BodyHtml,
            Status = result.Success ? PersonEmailStatus.Sent : PersonEmailStatus.Failed,
            ProviderMessageId = Truncate(result.ProviderMessageId, PersonEmailMessage.MaxProviderMessageIdLength),
            ErrorMessage = Truncate(result.Error, PersonEmailMessage.MaxErrorMessageLength),
            SentByUserId = actor.UserId,
            SentAt = sentAt
        };
        await unitOfWork.GetRepository<PersonEmailMessage>().InsertAsync(message);

        foreach (var attachment in attachments)
        {
            await unitOfWork.GetRepository<PersonEmailMessageAttachment>().InsertAsync(new PersonEmailMessageAttachment
            {
                CompanyId = actor.CompanyId,
                PersonEmailMessageId = message.Id,
                SourceKind = attachment.SourceKind,
                SourceId = attachment.SourceId,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                SizeBytes = attachment.Content.LongLength
            });
        }

        LeadInteraction? interaction = null;
        if (result.Success && lead is not null)
        {
            interaction = new LeadInteraction
            {
                CompanyId = actor.CompanyId,
                LeadId = lead.Id,
                ContactTypeCompanyId = emailContactTypeId!.Value,
                Direction = InteractionDirection.Outbound,
                OccurredAt = sentAt,
                UserId = actor.UserId,
                Outcome = InteractionOutcome.Other,
                Summary = Summary(rendered.Subject, rendered.BodyHtml),
                PersonEmailMessageId = message.Id
            };
            await unitOfWork.GetRepository<LeadInteraction>().InsertAsync(interaction);
            message.LeadInteractionId = interaction.Id;
            LeadWorkRules.Touch(lead, sentAt);
            await unitOfWork.GetRepository<Lead>().UpdateAsync(lead);
        }

        await SaveWithOneRetryAsync(message, cancellationToken);

        var data = new PersonEmailSentDto
        {
            Id = message.Id,
            Status = (int)message.Status,
            SentAt = message.SentAt,
            ToAddress = message.ToAddress,
            LeadId = message.LeadId,
            LeadInteractionId = interaction?.Id,
            ProviderMessageId = message.ProviderMessageId,
            AttachmentCount = attachments.Count
        };

        return result.Success
            ? new Response<PersonEmailSentDto> { IsSuccess = true, Message = "Email sent successfully.", Data = data }
            : new Response<PersonEmailSentDto>
            {
                IsSuccess = false,
                Message = PersonEmailErrorCodes.EmailSendFailed,
                Errors = [$"The email provider rejected the message: {message.ErrorMessage}", $"PersonEmailMessageId: {message.Id}"],
                Data = data
            };
    }

    /// <summary>"Correo: {subject}" plus a plain-text excerpt of the body, within the 2000 characters of the contact.</summary>
    public static string Summary(string subject, string bodyHtml)
    {
        var head = $"Correo: {subject}";
        var room = SummaryMaxLength - head.Length - 3;
        var excerpt = room > 0 ? EmailPlainText.Extract(bodyHtml, room) : string.Empty;
        var summary = excerpt.Length == 0 ? head : $"{head} — {excerpt}";
        return summary.Length <= SummaryMaxLength ? summary : summary[..SummaryMaxLength];
    }

    /// <summary>The active contact type of the company whose master type has the code <c>EMAIL</c>.</summary>
    private async Task<Guid?> EmailContactTypeAsync(Guid companyId)
    {
        var master = (await unitOfWork.GetRepository<LeadContactType>().GetAllIncludingDeletedAsync(
            t => t.Code == EmailContactTypeCode && t.GcRecord == 0 && t.IsActive)).Select(t => t.Id).ToList();
        if (master.Count == 0)
        {
            return null;
        }

        return (await unitOfWork.GetRepository<ContactTypeCompany>().GetAllIncludingDeletedAsync(
                c => c.CompanyId == companyId && c.GcRecord == 0 && c.IsActive && master.Contains(c.ContactTypeId)))
            .OrderBy(c => c.SortOrder)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefault();
    }

    private async Task<(List<LoadedAttachment> Attachments, Response<PersonEmailSentDto>? Error)> LoadAttachmentsAsync(
        IReadOnlyList<PersonEmailAttachmentInput> inputs, Guid companyId, Lead? lead, long maxTotalBytes, CancellationToken cancellationToken)
    {
        var loaded = new List<LoadedAttachment>();
        if (inputs.Count == 0)
        {
            return (loaded, null);
        }

        if (inputs.Count > MaxAttachments)
        {
            return (loaded, Error(PersonEmailErrorCodes.EmailAttachmentsTooLarge, $"Up to {MaxAttachments} attachments are allowed."));
        }

        var sources = new List<(PersonEmailAttachmentSourceKind Kind, Guid Id, string FileName, string ContentType, long Size, string Path, StorageProviderKind Provider)>();
        foreach (var input in inputs.DistinctBy(i => (i.SourceKind, i.SourceId)))
        {
            switch ((PersonEmailAttachmentSourceKind)input.SourceKind)
            {
                case PersonEmailAttachmentSourceKind.ProductDocument:
                    var productDocument = await unitOfWork.GetRepository<ProductDocument>().GetAsync(input.SourceId);
                    if (productDocument is null || productDocument.CompanyId != companyId || productDocument.GcRecord != 0)
                    {
                        return (loaded, AttachmentNotFound());
                    }

                    sources.Add((PersonEmailAttachmentSourceKind.ProductDocument, productDocument.Id, productDocument.OriginalName,
                        productDocument.ContentType, productDocument.SizeBytes, productDocument.Path, productDocument.StorageProvider));
                    break;
                case PersonEmailAttachmentSourceKind.LeadDocument:
                    var leadDocument = await unitOfWork.GetRepository<LeadDocument>().GetAsync(input.SourceId);
                    if (lead is null || leadDocument is null || leadDocument.CompanyId != companyId || leadDocument.GcRecord != 0 || leadDocument.LeadId != lead.Id)
                    {
                        return (loaded, AttachmentNotFound());
                    }

                    sources.Add((PersonEmailAttachmentSourceKind.LeadDocument, leadDocument.Id, leadDocument.OriginalName,
                        leadDocument.ContentType, leadDocument.SizeBytes, leadDocument.Path, leadDocument.StorageProvider));
                    break;
                default:
                    return (loaded, AttachmentNotFound());
            }
        }

        if (sources.Sum(s => s.Size) > maxTotalBytes)
        {
            return (loaded, Error(PersonEmailErrorCodes.EmailAttachmentsTooLarge, $"The attachments exceed the total of {maxTotalBytes} bytes of the company."));
        }

        foreach (var source in sources)
        {
            var provider = fileStorageResolver.Resolve(source.Provider);
            if (provider is null)
            {
                return (loaded, Error(PersonEmailErrorCodes.StorageProviderNotConfigured, $"The storage provider '{source.Provider}' of '{source.FileName}' is not configured."));
            }

            await using var stream = await provider.OpenReadAsync(source.Path, cancellationToken);
            if (stream is null)
            {
                return (loaded, Error(PersonEmailErrorCodes.FileNotFoundInStorage, $"The file '{source.FileName}' is missing from storage."));
            }

            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            loaded.Add(new LoadedAttachment(source.Kind, source.Id, source.FileName, source.ContentType, buffer.ToArray()));
        }

        // The real bytes decide, in case a row carries a stale size.
        return loaded.Sum(a => a.Content.LongLength) > maxTotalBytes
            ? ([], Error(PersonEmailErrorCodes.EmailAttachmentsTooLarge, $"The attachments exceed the total of {maxTotalBytes} bytes of the company."))
            : (loaded, null);
    }

    /// <summary>Risks table: the email already left, so a failed save is retried once and logged with the provider id.</summary>
    private async Task SaveWithOneRetryAsync(PersonEmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[PersonEmailSender] Saving the email {MessageId} (provider id {ProviderMessageId}) failed; retrying once.",
                message.Id, message.ProviderMessageId);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];

    private static Response<PersonEmailSentDto> Error(string code, string detail) => Response<PersonEmailSentDto>.Error(code, [detail]);

    private static Response<PersonEmailSentDto> AttachmentNotFound() =>
        Error(PersonEmailErrorCodes.EmailAttachmentNotFound, "The document does not exist, is deleted or belongs to another lead.");

    private sealed record LoadedAttachment(PersonEmailAttachmentSourceKind SourceKind, Guid SourceId, string FileName, string ContentType, byte[] Content);
}

/// <summary>SPEC 72 rule 1 — the valid e-mail addresses of a person: live, active e-mail contacts with a valid format.</summary>
public static class PersonEmailAddresses
{
    public static async Task<IReadOnlyList<PersonEmailAddressDto>> ValidAsync(IUnitOfWork unitOfWork, Guid companyId, Guid personId) =>
        Filter((await unitOfWork.GetRepository<PersonContact>().GetAllIncludingDeletedAsync(
                c => c.CompanyId == companyId && c.PersonId == personId && c.GcRecord == 0 && c.IsActive
                    && (c.ContactType == PersonContactType.PrimaryEmail || c.ContactType == PersonContactType.AlternativeEmail)))
            .Select(c => new PersonEmailAddressDto { Id = c.Id, Value = c.ContactValue.Trim(), IsPrimary = c.IsPrimary, ContactType = c.ContactType.ToString() }));

    /// <summary>Drops the invalid values and orders the primary first (then the primary e-mail type).</summary>
    public static IReadOnlyList<PersonEmailAddressDto> Filter(IEnumerable<PersonEmailAddressDto> contacts) =>
        contacts
            .Where(c => EmailAddressValidator.IsValid(c.Value))
            .OrderByDescending(c => c.IsPrimary)
            .ThenBy(c => c.ContactType == nameof(PersonContactType.PrimaryEmail) ? 0 : 1)
            .ThenBy(c => c.Value, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(c => c.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
