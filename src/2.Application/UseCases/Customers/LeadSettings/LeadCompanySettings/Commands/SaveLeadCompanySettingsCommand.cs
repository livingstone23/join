using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Queries;
using JOIN.Domain.Customers.Leads;
using MediatR;
using Settings = JOIN.Domain.Customers.Leads.LeadCompanySettings;

namespace JOIN.Application.UseCases.Customers.LeadSettings.LeadCompanySettings.Commands;

/// <summary>
/// <c>PUT /LeadCompanySettings</c> (SPEC 59): creates or updates the lead rules of the token's company.
/// </summary>
public sealed record SaveLeadCompanySettingsCommand : ITransactionalCommand<Response<LeadCompanySettingsDto>>
{
    /// <summary><c>DuplicateOpenLeadPolicy</c>: 1 Warn, 2 Block, 3 Allow.</summary>
    public int DuplicateOpenLeadPolicy { get; init; } = 1;
    public bool RequireIdentificationForCustomer { get; init; } = true;
    public string CodePrefix { get; init; } = Settings.DefaultCodePrefix;
    public int CodeSequenceLength { get; init; } = Settings.DefaultCodeSequenceLength;
}

/// <summary>Validates <see cref="SaveLeadCompanySettingsCommand"/>.</summary>
public sealed class SaveLeadCompanySettingsCommandValidator : AbstractValidator<SaveLeadCompanySettingsCommand>
{
    public SaveLeadCompanySettingsCommandValidator()
    {
        RuleFor(x => x.DuplicateOpenLeadPolicy)
            .Must(v => Enum.IsDefined(typeof(DuplicateOpenLeadPolicy), v))
            .WithMessage("DuplicateOpenLeadPolicy must be 1 (Warn), 2 (Block) or 3 (Allow).");
        RuleFor(x => x.CodePrefix)
            .Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage("CodePrefix is required.")
            .Must(p => (p ?? string.Empty).Trim().Length <= 10).WithMessage("CodePrefix cannot exceed 10 characters.");
        RuleFor(x => x.CodeSequenceLength).InclusiveBetween(3, 10);
    }
}

/// <summary>Handles <see cref="SaveLeadCompanySettingsCommand"/>.</summary>
public sealed class SaveLeadCompanySettingsCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<SaveLeadCompanySettingsCommand, Response<LeadCompanySettingsDto>>
{
    public async Task<Response<LeadCompanySettingsDto>> Handle(SaveLeadCompanySettingsCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogQuery.CompanyRequired<LeadCompanySettingsDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        var existingId = (await repository.GetAllIncludingDeletedAsync(s => s.CompanyId == companyId && s.GcRecord == 0)).FirstOrDefault()?.Id;
        var entity = existingId is { } id ? await repository.GetAsync(id) : null;
        var isNew = entity is null;
        entity ??= new Settings { CompanyId = companyId };

        entity.DuplicateOpenLeadPolicy = (DuplicateOpenLeadPolicy)request.DuplicateOpenLeadPolicy;
        entity.RequireIdentificationForCustomer = request.RequireIdentificationForCustomer;
        entity.CodePrefix = request.CodePrefix.Trim();
        entity.CodeSequenceLength = request.CodeSequenceLength;

        if (isNew)
        {
            await repository.InsertAsync(entity);
        }
        else
        {
            await repository.UpdateAsync(entity);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadCompanySettingsDto>
        {
            IsSuccess = true,
            Message = "Lead settings saved successfully.",
            Data = LeadCompanySettingsMapping.ToDto(entity, isDefault: false)
        };
    }
}
