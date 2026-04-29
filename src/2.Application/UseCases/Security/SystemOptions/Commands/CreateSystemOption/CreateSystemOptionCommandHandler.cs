using System.Threading;
using System.Threading.Tasks;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Mappings.Security.SystemOption;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Handler for creating a new SystemOption (patrón TimeUnit).
/// </summary>
public sealed class CreateSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    ISystemOptionMapper mapper)
    : IRequestHandler<CreateSystemOptionCommand, Response<SystemOptionDto>>
{
    public async Task<Response<SystemOptionDto>> Handle(CreateSystemOptionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<SystemOption>();
        var entity = mapper.ToEntity(request);
        await repository.InsertAsync(entity);
        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return new Response<SystemOptionDto>
            {
                IsSuccess = false,
                Message = "CREATE_FAILED",
                Errors = new[] { "No records were affected while creating the system option." }
            };
        }
        var dto = mapper.ToDto(entity);
        return new Response<SystemOptionDto>
        {
            IsSuccess = true,
            Message = "System option created successfully.",
            Data = dto
        };
    }
}
