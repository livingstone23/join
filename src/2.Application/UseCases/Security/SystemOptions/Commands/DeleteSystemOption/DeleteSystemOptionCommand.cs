using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Command for performing a soft delete on a system option, identified by its <paramref name="Id"/>.
/// </summary>
public sealed record DeleteSystemOptionCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;
