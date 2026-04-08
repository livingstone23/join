using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Command used to update an existing tenant-scoped functional area.
/// </summary>
public sealed record UpdateAreaCommand : IRequest<Response<AreaDto>>
{
    /// <summary>
    /// Gets or sets the unique identifier of the target area.
    /// This value is populated by the API layer from the route and is not expected in the JSON payload.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the unique identifier of the company that owns the area.
    /// This value is populated by the API layer from the <c>X-Company-Id</c> header and is not expected in the JSON payload.
    /// </summary>
    [JsonIgnore]
    public Guid CompanyId { get; init; }

    /// <summary>
    /// Gets or sets the business name of the area.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the foreign key for the current entity status.
    /// </summary>
    public Guid EntityStatusId { get; init; }
}
