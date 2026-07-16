using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed record DeleteIncomeRangeCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
