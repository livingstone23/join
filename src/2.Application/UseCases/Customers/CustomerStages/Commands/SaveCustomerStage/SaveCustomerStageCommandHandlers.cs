using JOIN.Application.Common;
using JOIN.Application.DTO.Customers;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.CustomerStages.Commands;

/// <summary>
/// Handles <see cref="CreateCustomerStageCommand"/>.
/// </summary>
public sealed class CreateCustomerStageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CustomerStageDefaultCoordinator defaultCoordinator)
    : IRequestHandler<CreateCustomerStageCommand, Response<CustomerStageDto>>
{
    public async Task<Response<CustomerStageDto>> Handle(CreateCustomerStageCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CustomerStageRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var liveStages = await CustomerStageRules.LoadLiveStagesAsync(unitOfWork, companyId);

        if (CustomerStageRules.Validate(request, liveStages, existing: null) is { } error)
        {
            return error;
        }

        if (request.IsDefault)
        {
            await defaultCoordinator.ClearOtherDefaultsAsync(companyId, Guid.Empty, cancellationToken);
        }

        var entity = CustomerStage.Create(
            companyId,
            request.Code,
            request.Name,
            request.Description,
            request.SortOrder,
            request.IsDefault,
            request.IsActive);

        await unitOfWork.GetRepository<CustomerStage>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CustomerStageDto>
        {
            IsSuccess = true,
            Message = "Customer stage created successfully.",
            Data = CustomerStageRules.ToDto(entity, customersCount: 0)
        };
    }
}

/// <summary>
/// Handles <see cref="UpdateCustomerStageCommand"/>.
/// </summary>
public sealed class UpdateCustomerStageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CustomerStageDefaultCoordinator defaultCoordinator)
    : IRequestHandler<UpdateCustomerStageCommand, Response<CustomerStageDto>>
{
    public async Task<Response<CustomerStageDto>> Handle(UpdateCustomerStageCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CustomerStageRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<CustomerStage>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<CustomerStageDto>.Error(CustomerErrorCodes.CustomerStageNotFound, ["Customer stage not found."]);
        }

        var liveStages = await CustomerStageRules.LoadLiveStagesAsync(unitOfWork, companyId);
        if (CustomerStageRules.Validate(request, liveStages, entity) is { } error)
        {
            return error;
        }

        if (request.IsDefault && !entity.IsDefault)
        {
            await defaultCoordinator.ClearOtherDefaultsAsync(companyId, entity.Id, cancellationToken);
            entity.MarkAsDefault();
        }

        entity.Update(request.Code, request.Name, request.Description, request.SortOrder);
        if (request.IsActive)
        {
            entity.Activate();
        }
        else
        {
            entity.Deactivate();
        }

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var customersCount = await CustomerStageRules.CountCustomersAsync(unitOfWork, entity.Id);
        return new Response<CustomerStageDto>
        {
            IsSuccess = true,
            Message = "Customer stage updated successfully.",
            Data = CustomerStageRules.ToDto(entity, customersCount)
        };
    }
}

/// <summary>
/// Handles <see cref="DeleteCustomerStageCommand"/>.
/// </summary>
public sealed class DeleteCustomerStageCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteCustomerStageCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteCustomerStageCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CustomerErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CustomerStage>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(CustomerErrorCodes.CustomerStageNotFound, ["Customer stage not found."]);
        }

        if (entity.IsDefault)
        {
            return Response<Guid>.Error(
                CustomerErrorCodes.CustomerStageDefaultRequired,
                ["The default stage cannot be deleted. Mark another stage as default first."]);
        }

        var customersCount = await CustomerStageRules.CountCustomersAsync(unitOfWork, entity.Id);
        if (customersCount > 0)
        {
            return Response<Guid>.Error(
                CustomerErrorCodes.CustomerStageInUse,
                [$"The stage is used by {customersCount} customer(s)."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Customer stage deleted successfully.",
            Data = entity.Id
        };
    }
}

/// <summary>
/// Handles <see cref="RestoreCustomerStageCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).
/// </summary>
public sealed class RestoreCustomerStageCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreCustomerStageCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreCustomerStageCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CustomerErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error("SUPERADMIN_REQUIRED", ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<CustomerStage>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<CustomerStage>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && (x.Code == entity.Code || x.Name == entity.Name)),
            beforeRestore: async (entity, _) =>
            {
                if (entity.IsDefault && await restorer.AnyAsync<CustomerStage>(
                        x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.IsDefault))
                {
                    entity.UnmarkAsDefault();
                }
            },
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Rules shared by the customer stage commands.
/// </summary>
internal static class CustomerStageRules
{
    internal static Response<CustomerStageDto> CompanyRequired() =>
        Response<CustomerStageDto>.Error(CustomerErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static async Task<List<CustomerStage>> LoadLiveStagesAsync(IUnitOfWork unitOfWork, Guid companyId) =>
        (await unitOfWork.GetRepository<CustomerStage>()
            .GetAllIncludingDeletedAsync(s => s.CompanyId == companyId && s.GcRecord == 0))
        .ToList();

    internal static async Task<int> CountCustomersAsync(IUnitOfWork unitOfWork, Guid stageId) =>
        (await unitOfWork.GetRepository<Customer>()
            .GetAllIncludingDeletedAsync(c => c.CustomerStageId == stageId && c.GcRecord == 0))
        .Count();

    /// <summary>
    /// Default-stage and uniqueness rules (SPEC 55 rule 8): returns the error, or <c>null</c> when the input is valid.
    /// </summary>
    internal static Response<CustomerStageDto>? Validate(ICustomerStageInput input, IReadOnlyCollection<CustomerStage> liveStages, CustomerStage? existing)
    {
        if (input.IsDefault && !input.IsActive)
        {
            return Response<CustomerStageDto>.Error(
                CustomerErrorCodes.CustomerStageDefaultInactive,
                ["The default stage must be active."]);
        }

        var companyHasOtherDefault = liveStages.Any(s => s.IsDefault && s.Id != existing?.Id);
        if (!input.IsDefault && !companyHasOtherDefault)
        {
            return Response<CustomerStageDto>.Error(
                CustomerErrorCodes.CustomerStageDefaultRequired,
                ["The company must keep one default stage. Mark another stage as default first."]);
        }

        var code = CustomerStage.NormalizeCode(input.Code);
        var name = input.Name.Trim();
        var duplicate = liveStages.Any(s => s.Id != existing?.Id
            && (string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)));

        return duplicate
            ? Response<CustomerStageDto>.Error(
                CustomerErrorCodes.DuplicateKey,
                ["Another stage of the company already uses the same code or name."])
            : null;
    }

    internal static CustomerStageDto ToDto(CustomerStage entity, int customersCount) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Code = entity.Code,
        Name = entity.Name,
        Description = entity.Description,
        SortOrder = entity.SortOrder,
        IsDefault = entity.IsDefault,
        IsActive = entity.IsActive,
        CustomersCount = customersCount,
        CreatedAt = entity.Created
    };
}
