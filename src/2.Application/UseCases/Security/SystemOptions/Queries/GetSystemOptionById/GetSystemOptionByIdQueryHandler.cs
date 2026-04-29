using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Exceptions;
using MediatR;



namespace JOIN.Application.UseCases.Security.SystemOptions.Queries;



/// <summary>
/// Handler for getting a SystemOption by Id using Dapper.
/// </summary>
public sealed class GetSystemOptionByIdQueryHandler(
    ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetSystemOptionByIdQuery, Response<SystemOptionDto>>
{
    public async Task<Response<SystemOptionDto>> Handle(GetSystemOptionByIdQuery request, CancellationToken cancellationToken)
    {

        
        using var connection = connectionFactory.CreateConnection();

        const string sql = @"SELECT Id, ModuleId, Name, Route, Icon, ParentId, ControllerName, CanRead, CanCreate, CanUpdate, CanDelete, Created, ModuleName = '', ParentName = ''
                             FROM Security.SystemOptions
                             WHERE Id = @Id AND GcRecord = 0;";

        var entity = await connection.QuerySingleOrDefaultAsync<SystemOptionDto>(sql, new { request.Id });
        
        if (entity is null)
        {
            throw new NotFoundException("TimeUnit", request.Id, "Time unit not found.");
        }

        return new Response<SystemOptionDto>
        {
            IsSuccess = true,
            Message = "Time unit retrieved successfully.",
            Data = entity
        };
    }
}
