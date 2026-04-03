using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Queries;

/// <summary>
/// Query to retrieve a paginated list of communication channels with optional search.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested page size.</param>
/// <param name="SearchTerm">Optional search term for name, provider or code.</param>
public record GetCommunicationChannelsPagedQuery(int PageNumber = 1, int PageSize = 10, string? SearchTerm = null)
    : IRequest<Response<PagedResult<CommunicationChannelListItemDto>>>;
