using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Queries;

public sealed record GetIncomeRangeByIdQuery(Guid Id) : IRequest<Response<IncomeRangeDto>>;
