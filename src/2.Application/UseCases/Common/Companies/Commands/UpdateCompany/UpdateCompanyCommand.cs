using System.Text.Json.Serialization;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Commands;

/// <summary>
/// Command to update an existing company.
/// </summary>
public record UpdateCompanyCommand : IRequest<Response<CompanyDto>>
{
    /// <summary>
    /// Gets the company identifier to update.
    /// </summary>
    [JsonIgnore]
    public Guid Id { get; init; }

    /// <summary>
    /// Gets or sets the legal company name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the company description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets or sets the company tax identifier.
    /// </summary>
    public string TaxId { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the company email.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// Gets or sets the company phone.
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    /// Gets or sets the company website.
    /// </summary>
    public string? WebSite { get; init; }

    /// <summary>
    /// Gets or sets whether the company is active.
    /// </summary>
    public bool IsActive { get; init; } = true;
}
