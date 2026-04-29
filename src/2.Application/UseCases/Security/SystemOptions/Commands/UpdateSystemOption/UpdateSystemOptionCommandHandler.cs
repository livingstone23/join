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
/// Handler for updating an existing SystemOption (patrón TimeUnit).
/// </summary>
public sealed class UpdateSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    ISystemOptionMapper mapper)
    : IRequestHandler<UpdateSystemOptionCommand, Response<SystemOptionDto>>
{
    public async Task<Response<SystemOptionDto>> Handle(UpdateSystemOptionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<SystemOption>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null)
        {
            return new Response<SystemOptionDto>
            {
                IsSuccess = false,
                Message = "SYSTEM_OPTION_NOT_FOUND",
                Errors = new[] { "System option not found." }
            };
        }

        // Validación de nombre único (opcional, si aplica)
        // var existingOptions = await repository.GetAllAsync();
        // var nameInUse = existingOptions.Any(opt =>
        //     opt.Id != request.Id && opt.GcRecord == 0 && string.Equals(opt.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase));
        // if (nameInUse)
        // {
        //     return new Response<SystemOptionDto>
        //     {
        //         IsSuccess = false,
        //         Message = "SYSTEM_OPTION_NAME_IN_USE",
        //         Errors = new[] { "Another active system option already uses the same name." }
        //     };
        // }

        mapper.ApplyUpdate(request, entity);
        await repository.UpdateAsync(entity);
        var result = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return new Response<SystemOptionDto>
            {
                IsSuccess = false,
                Message = "UPDATE_FAILED",
                Errors = new[] { "No records were affected while updating the system option." }
            };
        }
        var dto = mapper.ToDto(entity);
        return new Response<SystemOptionDto>
        {
            IsSuccess = true,
            Message = "System option updated successfully.",
            Data = dto
        };
    }
}
