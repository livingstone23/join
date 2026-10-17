using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Commands;

/// <summary>
/// <c>PUT /ContactTypeCompanies/{id}</c> (SPEC 59): the company renames, orders, activates or deactivates its copy of a
/// master contact type. It never changes the code nor another company's row.
/// </summary>
public sealed record UpdateContactTypeCompanyCommand : ITransactionalCommand<Response<ContactTypeCompanyDto>>
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>Validates <see cref="UpdateContactTypeCompanyCommand"/>.</summary>
public sealed class UpdateContactTypeCompanyCommandValidator : AbstractValidator<UpdateContactTypeCompanyCommand>
{
    public UpdateContactTypeCompanyCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

/// <summary>Handles <see cref="UpdateContactTypeCompanyCommand"/>.</summary>
public sealed class UpdateContactTypeCompanyCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<UpdateContactTypeCompanyCommand, Response<ContactTypeCompanyDto>>
{
    public async Task<Response<ContactTypeCompanyDto>> Handle(UpdateContactTypeCompanyCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogQuery.CompanyRequired<ContactTypeCompanyDto>();
        }

        var repository = unitOfWork.GetRepository<ContactTypeCompany>();
        var entity = await repository.GetAsync(request.Id);
        var master = entity is null ? null : await unitOfWork.GetRepository<ContactType>().GetAsync(entity.ContactTypeId);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0 || master is null || master.GcRecord != 0)
        {
            return Response<ContactTypeCompanyDto>.Error(LeadSettingsErrorCodes.ContactTypeNotFound, ["The contact type was not found."]);
        }

        entity.Name = request.Name.Trim();
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ContactTypeCompanyDto>
        {
            IsSuccess = true,
            Message = "The company contact type was updated successfully.",
            Data = new ContactTypeCompanyDto
            {
                Id = entity.Id,
                CompanyId = entity.CompanyId,
                ContactTypeId = master.Id,
                Code = master.Code,
                Name = entity.Name,
                MasterName = master.Name,
                Icon = master.Icon,
                CommunicationChannelCode = master.CommunicationChannelCode,
                SchedulesCalendarEvent = master.SchedulesCalendarEvent,
                AsksDuration = master.AsksDuration,
                SortOrder = entity.SortOrder,
                IsActive = entity.IsActive
            }
        };
    }
}
