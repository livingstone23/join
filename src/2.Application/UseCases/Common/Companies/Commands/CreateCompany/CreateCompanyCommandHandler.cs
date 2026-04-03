using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Handles company creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class CreateCompanyCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCompanyCommand, Response<CompanyDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a company.
    /// </summary>
    public async Task<Response<CompanyDto>> Handle(CreateCompanyCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<Company>();
        var normalizedTaxId = request.TaxId.Trim();

        var companies = await repository.GetAllAsync();
        var taxIdInUse = companies.Any(c => string.Equals(c.TaxId, normalizedTaxId, StringComparison.OrdinalIgnoreCase));
        if (taxIdInUse)
        {
            return Response<CompanyDto>.Error("COMPANY_TAXID_IN_USE", ["Another active company already uses the same TaxId."]);
        }

        var entity = new Company
        {
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            TaxId = normalizedTaxId,
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            WebSite = request.WebSite?.Trim(),
            IsActive = request.IsActive
        };

        await repository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<CompanyDto>.Error("CREATE_FAILED", ["No records were affected while creating the company."]);
        }

        return new Response<CompanyDto>
        {
            IsSuccess = true,
            Message = "Company created successfully.",
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
