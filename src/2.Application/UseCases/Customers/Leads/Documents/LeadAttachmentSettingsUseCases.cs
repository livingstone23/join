using Dapper;
using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using JOIN.Domain.Customers.Leads;
using JOIN.Domain.Enums;
using MediatR;
using Settings = JOIN.Domain.Customers.Leads.LeadAttachmentSettings;

namespace JOIN.Application.UseCases.Customers.Leads.Documents;

/// <summary>Fields of the company's lead attachment limits.</summary>
public interface ILeadAttachmentSettingsInput
{
    /// <summary><c>DocumentType</c> flags: Pdf 1, Word 2, Excel 4, Text 8, Image 16, Other 32.</summary>
    int AllowedDocumentTypes { get; }
    long MaxFileSizeBytes { get; }
    int MaxFilesPerInteraction { get; }
    int? MaxFilesPerDay { get; }
}

/// <summary><c>GET /LeadAttachmentSettings</c> (SPEC 62): the company's row; <c>LEAD_ATTACHMENT_SETTINGS_NOT_CONFIGURED</c> without one.</summary>
public sealed record GetLeadAttachmentSettingsQuery(Guid? CompanyId = null) : IRequest<Response<LeadAttachmentSettingsDto>>;

/// <summary><c>POST /LeadAttachmentSettings</c>: creates the row of the token's company (409 when it exists).</summary>
public sealed record CreateLeadAttachmentSettingsCommand : ILeadAttachmentSettingsInput, ITransactionalCommand<Response<LeadAttachmentSettingsDto>>
{
    public int AllowedDocumentTypes { get; init; }
    public long MaxFileSizeBytes { get; init; }
    public int MaxFilesPerInteraction { get; init; }
    public int? MaxFilesPerDay { get; init; }
}

/// <summary><c>PUT /LeadAttachmentSettings</c>: updates the row of the token's company.</summary>
public sealed record UpdateLeadAttachmentSettingsCommand : ILeadAttachmentSettingsInput, ITransactionalCommand<Response<LeadAttachmentSettingsDto>>
{
    public int AllowedDocumentTypes { get; init; }
    public long MaxFileSizeBytes { get; init; }
    public int MaxFilesPerInteraction { get; init; }
    public int? MaxFilesPerDay { get; init; }
}

internal static class LeadAttachmentSettingsRules
{
    private const int AllTypes = (int)(DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Text | DocumentType.Image | DocumentType.Other);

    internal static void Apply<T>(AbstractValidator<T> validator) where T : ILeadAttachmentSettingsInput
    {
        validator.RuleFor(x => x.AllowedDocumentTypes).Must(v => v > 0 && (v & ~AllTypes) == 0).WithMessage("AllowedDocumentTypes must combine known document types.");
        validator.RuleFor(x => x.MaxFileSizeBytes).InclusiveBetween(1, 100L * 1024 * 1024);
        validator.RuleFor(x => x.MaxFilesPerInteraction).InclusiveBetween(1, 100);
        validator.RuleFor(x => x.MaxFilesPerDay).InclusiveBetween(1, 100000).When(x => x.MaxFilesPerDay.HasValue);
    }

    internal static void Copy(Settings entity, ILeadAttachmentSettingsInput input)
    {
        entity.AllowedDocumentTypes = (DocumentType)input.AllowedDocumentTypes;
        entity.MaxFileSizeBytes = input.MaxFileSizeBytes;
        entity.MaxFilesPerInteraction = input.MaxFilesPerInteraction;
        entity.MaxFilesPerDay = input.MaxFilesPerDay;
    }

    internal static LeadAttachmentSettingsDto ToDto(Guid id, Guid companyId, DocumentType types, long maxSize, int perInteraction, int? perDay) => new()
    {
        Id = id,
        CompanyId = companyId,
        AllowedDocumentTypes = (int)types,
        AllowedDocumentTypeNames = Enum.GetValues<DocumentType>().Where(t => t != DocumentType.None && types.HasFlag(t)).Select(t => t.ToString()).ToList(),
        MaxFileSizeBytes = maxSize,
        MaxFilesPerInteraction = perInteraction,
        MaxFilesPerDay = perDay
    };

    internal static LeadAttachmentSettingsDto ToDto(Settings s) =>
        ToDto(s.Id, s.CompanyId, s.AllowedDocumentTypes, s.MaxFileSizeBytes, s.MaxFilesPerInteraction, s.MaxFilesPerDay);
}

/// <summary>Validates <see cref="CreateLeadAttachmentSettingsCommand"/>.</summary>
public sealed class CreateLeadAttachmentSettingsCommandValidator : AbstractValidator<CreateLeadAttachmentSettingsCommand>
{
    public CreateLeadAttachmentSettingsCommandValidator() => LeadAttachmentSettingsRules.Apply(this);
}

/// <summary>Validates <see cref="UpdateLeadAttachmentSettingsCommand"/>.</summary>
public sealed class UpdateLeadAttachmentSettingsCommandValidator : AbstractValidator<UpdateLeadAttachmentSettingsCommand>
{
    public UpdateLeadAttachmentSettingsCommandValidator() => LeadAttachmentSettingsRules.Apply(this);
}

/// <summary>Handles <see cref="GetLeadAttachmentSettingsQuery"/>.</summary>
public sealed class GetLeadAttachmentSettingsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetLeadAttachmentSettingsQuery, Response<LeadAttachmentSettingsDto>>
{
    public async Task<Response<LeadAttachmentSettingsDto>> Handle(GetLeadAttachmentSettingsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogQuery.CompanyRequired<LeadAttachmentSettingsDto>();
        }

        const string sql = """
            SELECT s.Id, s.CompanyId, s.AllowedDocumentTypes, s.MaxFileSizeBytes, s.MaxFilesPerInteraction, s.MaxFilesPerDay
            FROM customer.leadattachmentsettings s
            WHERE s.CompanyId = @CompanyId
              AND s.GcRecord = 0;
            """;

        using var connection = connectionFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            sql, new { CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) }, cancellationToken: cancellationToken));

        return row is null
            ? Response<LeadAttachmentSettingsDto>.Error(LeadErrorCodes.AttachmentSettingsNotConfigured, ["The company has no lead attachment settings."])
            : new Response<LeadAttachmentSettingsDto>
            {
                IsSuccess = true,
                Message = "Lead attachment settings retrieved successfully.",
                Data = LeadAttachmentSettingsRules.ToDto(row.Id, row.CompanyId, (DocumentType)row.AllowedDocumentTypes, row.MaxFileSizeBytes, row.MaxFilesPerInteraction, row.MaxFilesPerDay)
            };
    }

    private sealed class Row
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public int AllowedDocumentTypes { get; set; }
        public long MaxFileSizeBytes { get; set; }
        public int MaxFilesPerInteraction { get; set; }
        public int? MaxFilesPerDay { get; set; }
    }
}

/// <summary>Handles <see cref="CreateLeadAttachmentSettingsCommand"/>.</summary>
public sealed class CreateLeadAttachmentSettingsCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<CreateLeadAttachmentSettingsCommand, Response<LeadAttachmentSettingsDto>>
{
    public async Task<Response<LeadAttachmentSettingsDto>> Handle(CreateLeadAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return CommercialCatalogQuery.CompanyRequired<LeadAttachmentSettingsDto>();
        }

        var repository = unitOfWork.GetRepository<Settings>();
        if ((await repository.GetAllIncludingDeletedAsync(s => s.CompanyId == companyId && s.GcRecord == 0)).Any())
        {
            return Response<LeadAttachmentSettingsDto>.Error(LeadErrorCodes.AttachmentSettingsAlreadyExist, ["The company already has lead attachment settings; update them instead."]);
        }

        var entity = new Settings { CompanyId = companyId };
        LeadAttachmentSettingsRules.Copy(entity, request);
        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadAttachmentSettingsDto> { IsSuccess = true, Message = "Lead attachment settings created successfully.", Data = LeadAttachmentSettingsRules.ToDto(entity) };
    }
}

/// <summary>Handles <see cref="UpdateLeadAttachmentSettingsCommand"/>.</summary>
public sealed class UpdateLeadAttachmentSettingsCommandHandler(IUnitOfWork unitOfWork, ICurrentUserService currentUserService)
    : IRequestHandler<UpdateLeadAttachmentSettingsCommand, Response<LeadAttachmentSettingsDto>>
{
    public async Task<Response<LeadAttachmentSettingsDto>> Handle(UpdateLeadAttachmentSettingsCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return CommercialCatalogQuery.CompanyRequired<LeadAttachmentSettingsDto>();
        }

        var repository = unitOfWork.GetRepository<Settings>();
        var id = (await repository.GetAllIncludingDeletedAsync(s => s.CompanyId == companyId && s.GcRecord == 0)).FirstOrDefault()?.Id;
        var entity = id is { } existing ? await repository.GetAsync(existing) : null;
        if (entity is null)
        {
            return Response<LeadAttachmentSettingsDto>.Error(LeadErrorCodes.AttachmentSettingsNotConfigured, ["The company has no lead attachment settings; create them first."]);
        }

        LeadAttachmentSettingsRules.Copy(entity, request);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Response<LeadAttachmentSettingsDto> { IsSuccess = true, Message = "Lead attachment settings updated successfully.", Data = LeadAttachmentSettingsRules.ToDto(entity) };
    }
}
