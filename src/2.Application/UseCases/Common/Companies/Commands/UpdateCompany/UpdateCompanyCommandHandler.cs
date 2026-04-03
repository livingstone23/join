using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Handles company update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class UpdateCompanyCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCompanyCommand, Response<CompanyDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates an existing company.
    /// </summary>
    public async Task<Response<CompanyDto>> Handle(UpdateCompanyCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<Company>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null)
        {
            return Response<CompanyDto>.Error("COMPANY_NOT_FOUND", ["Company not found."]);
        }

        var normalizedTaxId = request.TaxId.Trim();
        var companies = await repository.GetAllAsync();
        var taxIdInUse = companies.Any(c => c.Id != request.Id && string.Equals(c.TaxId, normalizedTaxId, StringComparison.OrdinalIgnoreCase));
        if (taxIdInUse)
        {
            return Response<CompanyDto>.Error("COMPANY_TAXID_IN_USE", ["Another active company already uses the same TaxId."]);
        }

        entity.Name = request.Name.Trim();
        entity.Description = request.Description?.Trim();
        entity.TaxId = normalizedTaxId;
        entity.Email = request.Email?.Trim();
        entity.Phone = request.Phone?.Trim();
        entity.WebSite = request.WebSite?.Trim();
        entity.IsActive = request.IsActive;

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CompanyDto>.Error("UPDATE_FAILED", ["No records were affected while updating the company."]);
        }

        return new Response<CompanyDto>
        {
            IsSuccess = true,
            Message = "Company updated successfully.",
            Data = new CompanyDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                TaxId = entity.TaxId,
                Email = entity.Email,
                Phone = entity.Phone,
                WebSite = entity.WebSite,
                IsActive = entity.IsActive
            }
        };
    }
}
