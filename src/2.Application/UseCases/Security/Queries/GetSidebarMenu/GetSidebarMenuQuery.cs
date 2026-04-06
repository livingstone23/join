using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetSidebarMenu;

/// <summary>
/// Retrieves the hierarchical sidebar menu for the authenticated user in the active company context.
/// </summary>
public sealed record GetSidebarMenuQuery : IRequest<Response<IReadOnlyCollection<MenuOptionResponse>>>;
