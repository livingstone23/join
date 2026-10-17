using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Commands;

/// <summary>Fields of a master contact type.</summary>
public interface IContactTypeInput
{
    string Code { get; }
    string Name { get; }
    string Icon { get; }
    string? CommunicationChannelCode { get; }
    bool SchedulesCalendarEvent { get; }
    bool AsksDuration { get; }
    int SortOrder { get; }
    bool IsActive { get; }
}

/// <summary>
/// <c>POST /ContactTypes</c> (SPEC 59, SuperAdmin): adds a master type and an inactive copy for every live company.
/// </summary>
public sealed record CreateContactTypeCommand : IContactTypeInput, ITransactionalCommand<Response<ContactTypeDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string? CommunicationChannelCode { get; init; }
    public bool SchedulesCalendarEvent { get; init; }
    public bool AsksDuration { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>PUT /ContactTypes/{id}</c> (SPEC 59, SuperAdmin).</summary>
public sealed record UpdateContactTypeCommand : IContactTypeInput, ITransactionalCommand<Response<ContactTypeDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Icon { get; init; } = string.Empty;
    public string? CommunicationChannelCode { get; init; }
    public bool SchedulesCalendarEvent { get; init; }
    public bool AsksDuration { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>DELETE /ContactTypes/{id}</c> (SPEC 59, SuperAdmin): logical delete. SPEC 61 adds the in-use block.</summary>
public sealed record DeleteContactTypeCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
