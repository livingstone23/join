using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using MediatR;

namespace JOIN.Application.UseCases.Common.Emails.Queries;

/// <summary><c>GET /EmailCompanySettings</c> (SPEC 72): the company's sending rules, or <c>EMAIL_SETTINGS_NOT_CONFIGURED</c>.</summary>
public sealed record GetEmailCompanySettingsQuery : IRequest<Response<EmailCompanySettingsDto>>;
