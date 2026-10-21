using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using MediatR;

namespace JOIN.Application.UseCases.Common.Emails.Commands;

/// <summary>Content of an email, shared by the three send commands (SPEC 72 C.10–C.12).</summary>
public interface IPersonEmailInput
{
    /// <summary>One of the valid addresses of the person; empty means its primary one.</summary>
    string? ToAddress { get; }
    IReadOnlyList<string> Cc { get; }
    string Subject { get; }
    string BodyHtml { get; }
    Guid? EmailTemplateId { get; }
    IReadOnlyList<PersonEmailAttachmentInput> Attachments { get; }
}

/// <summary>
/// <c>POST /Persons/{id}/emails</c> (SPEC 72): sends to the person; the lead is optional (one of its open leads in the
/// scope of the user). Not transactional: the delivery runs outside a transaction (B.7).
/// </summary>
public sealed record SendPersonEmailCommand : IPersonEmailInput, IRequest<Response<PersonEmailSentDto>>
{
    public Guid PersonId { get; init; }
    public string? ToAddress { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public Guid? EmailTemplateId { get; init; }
    public Guid? LeadId { get; init; }
    public IReadOnlyList<PersonEmailAttachmentInput> Attachments { get; init; } = [];
}

/// <summary><c>POST /Customers/{id}/emails</c> (SPEC 72): sends to the person of the customer, <c>Origin = Customer</c>.</summary>
public sealed record SendCustomerEmailCommand : IPersonEmailInput, IRequest<Response<PersonEmailSentDto>>
{
    public Guid CustomerId { get; init; }
    public string? ToAddress { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public Guid? EmailTemplateId { get; init; }
    public Guid? LeadId { get; init; }
    public IReadOnlyList<PersonEmailAttachmentInput> Attachments { get; init; } = [];
}

/// <summary><c>POST /Leads/{id}/emails</c> (SPEC 72): sends to the person of the lead, always associated to that lead.</summary>
public sealed record SendLeadEmailCommand : IPersonEmailInput, IRequest<Response<PersonEmailSentDto>>
{
    /// <summary>Comes from the route; a <c>leadId</c> in the body (the shared payload of FE-47) is ignored.</summary>
    [JsonIgnore]
    public Guid LeadId { get; init; }
    public string? ToAddress { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = [];
    public string Subject { get; init; } = string.Empty;
    public string BodyHtml { get; init; } = string.Empty;
    public Guid? EmailTemplateId { get; init; }
    public IReadOnlyList<PersonEmailAttachmentInput> Attachments { get; init; } = [];
}
