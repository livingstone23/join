using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Common;
using JOIN.Domain.Customers.Leads;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Commands;

/// <summary>Rules shared by the master contact type commands.</summary>
internal static class ContactTypeRules
{
    internal static Response<T>? Guard<T>(ICurrentUserService currentUserService) =>
        currentUserService.IsInRole("SuperAdmin")
            ? null
            : Response<T>.Error(LeadSettingsErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can manage the master contact types."]);

    internal static async Task<bool> CodeTakenAsync(IUnitOfWork unitOfWork, string code, Guid? exceptId) =>
        (await unitOfWork.GetRepository<ContactType>().GetAllIncludingDeletedAsync(t => t.GcRecord == 0 && t.Code == code && t.Id != exceptId)).Any();

    internal static Response<ContactTypeDto> Duplicate() =>
        Response<ContactTypeDto>.Error(LeadSettingsErrorCodes.DuplicateKey, ["Another contact type already uses the same code."]);

    internal static void Apply(ContactType entity, IContactTypeInput input)
    {
        entity.Code = CommercialCode.Normalize(input.Code);
        entity.Name = input.Name.Trim();
        entity.Icon = input.Icon.Trim();
        entity.CommunicationChannelCode = string.IsNullOrWhiteSpace(input.CommunicationChannelCode)
            ? null
            : CommercialCode.Normalize(input.CommunicationChannelCode);
        entity.SchedulesCalendarEvent = input.SchedulesCalendarEvent;
        entity.AsksDuration = input.AsksDuration;
        entity.SortOrder = input.SortOrder;
        entity.IsActive = input.IsActive;
    }

    internal static ContactTypeDto ToDto(ContactType entity) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        Code = entity.Code,
        Name = entity.Name,
        Icon = entity.Icon,
        CommunicationChannelCode = entity.CommunicationChannelCode,
        SchedulesCalendarEvent = entity.SchedulesCalendarEvent,
        AsksDuration = entity.AsksDuration,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        CreatedAt = entity.Created
    };
}

/// <summary>Handles <see cref="CreateContactTypeCommand"/>.</summary>
public sealed class CreateContactTypeCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<CreateContactTypeCommand, Response<ContactTypeDto>>
{
    public async Task<Response<ContactTypeDto>> Handle(CreateContactTypeCommand request, CancellationToken cancellationToken)
    {
        if (ContactTypeRules.Guard<ContactTypeDto>(currentUserService) is { } denied)
        {
            return denied;
        }

        if (await ContactTypeRules.CodeTakenAsync(unitOfWork, CommercialCode.Normalize(request.Code), null))
        {
            return ContactTypeRules.Duplicate();
        }

        var entity = new ContactType();
        ContactTypeRules.Apply(entity, request);
        await unitOfWork.GetRepository<ContactType>().InsertAsync(entity);

        // Rule 5: each company gets the new type inactive; it activates and renames its own copy.
        var companyRows = unitOfWork.GetRepository<ContactTypeCompany>();
        foreach (var company in await unitOfWork.GetRepository<Company>().GetAllIncludingDeletedAsync(c => c.GcRecord == 0))
        {
            await companyRows.InsertAsync(new ContactTypeCompany
            {
                CompanyId = company.Id,
                ContactTypeId = entity.Id,
                Name = entity.Name,
                SortOrder = entity.SortOrder,
                IsActive = false
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<ContactTypeDto> { IsSuccess = true, Message = "The contact type was created successfully.", Data = ContactTypeRules.ToDto(entity) };
    }
}

/// <summary>Handles <see cref="UpdateContactTypeCommand"/>.</summary>
public sealed class UpdateContactTypeCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<UpdateContactTypeCommand, Response<ContactTypeDto>>
{
    public async Task<Response<ContactTypeDto>> Handle(UpdateContactTypeCommand request, CancellationToken cancellationToken)
    {
        if (ContactTypeRules.Guard<ContactTypeDto>(currentUserService) is { } denied)
        {
            return denied;
        }

        var repository = unitOfWork.GetRepository<ContactType>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<ContactTypeDto>.Error(LeadSettingsErrorCodes.ContactTypeNotFound, ["The contact type was not found."]);
        }

        if (await ContactTypeRules.CodeTakenAsync(unitOfWork, CommercialCode.Normalize(request.Code), entity.Id))
        {
            return ContactTypeRules.Duplicate();
        }

        ContactTypeRules.Apply(entity, request);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<ContactTypeDto> { IsSuccess = true, Message = "The contact type was updated successfully.", Data = ContactTypeRules.ToDto(entity) };
    }
}

/// <summary>Handles <see cref="DeleteContactTypeCommand"/>.</summary>
public sealed class DeleteContactTypeCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<DeleteContactTypeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteContactTypeCommand request, CancellationToken cancellationToken)
    {
        if (ContactTypeRules.Guard<Guid>(currentUserService) is { } denied)
        {
            return denied;
        }

        var repository = unitOfWork.GetRepository<ContactType>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(LeadSettingsErrorCodes.ContactTypeNotFound, ["The contact type was not found."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<Guid> { IsSuccess = true, Message = "The contact type was deleted successfully.", Data = entity.Id };
    }
}
