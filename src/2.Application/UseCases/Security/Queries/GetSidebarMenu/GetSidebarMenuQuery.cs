using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetSidebarMenu;

/// <summary>
/// Represents the query used to resolve the hierarchical sidebar menu for the authenticated user in the active company context.
/// The resulting payload is intended to drive permission-aware navigation rendering in the client UI.
/// </summary>
public sealed record GetSidebarMenuQuery : IRequest<Response<IReadOnlyCollection<MenuOptionResponse>>>;
