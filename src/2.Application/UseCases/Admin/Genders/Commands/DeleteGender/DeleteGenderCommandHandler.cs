using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Commands;

/// <summary>
/// Handles soft delete operations for tenant-scoped genders.
/// </summary>
public sealed class DeleteGenderCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteGenderCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <inheritdoc />
    public async Task<Response<Guid>> Handle(DeleteGenderCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var genderRepository = _unitOfWork.GetRepository<Gender>();
        var personRepository = _unitOfWork.GetRepository<Person>();

        var entity = await genderRepository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error("GENDER_NOT_FOUND", ["Gender not found."]);
        }

        var persons = await personRepository.GetAllAsync();
        var isInUse = persons.Any(person =>
            person.CompanyId == companyId
            && person.GcRecord == 0
            && person.GenderId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("GENDER_IN_USE", ["The gender is currently assigned to one or more persons and cannot be deleted."]);
        }

        entity.MarkAsDeleted();

        await genderRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the gender."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Gender deleted successfully.",
            Data = entity.Id
        };
    }
}
