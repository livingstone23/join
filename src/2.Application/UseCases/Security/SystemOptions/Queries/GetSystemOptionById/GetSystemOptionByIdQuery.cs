
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;



namespace JOIN.Application.UseCases.Security.SystemOptions.Queries;



/// <summary>
/// Query to get a SystemOption by its Id.
/// </summary>
public sealed record GetSystemOptionByIdQuery(Guid Id) : IRequest<Response<SystemOptionDto>>;
