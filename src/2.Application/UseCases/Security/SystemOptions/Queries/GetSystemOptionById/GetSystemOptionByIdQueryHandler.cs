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

        const string sql = @"SELECT o.Id, o.ModuleId, m.Name AS ModuleName, o.Name, o.Route, o.Icon,
                                    o.ParentId, p.Name AS ParentName, o.ControllerName,
                                    o.CanRead, o.CanCreate, o.CanUpdate, o.CanDelete,
                                    o.CanDownload, o.CanExport, o.CanExecute,
                                    o.IsVisibleMenu, o.OrderMenu, o.Created
                             FROM Security.SystemOptions o
                             INNER JOIN [Admin].[SystemModules] m ON m.Id = o.ModuleId
                             LEFT JOIN Security.SystemOptions p ON p.Id = o.ParentId AND p.GcRecord = 0
                             WHERE o.Id = @Id AND o.GcRecord = 0;";

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
