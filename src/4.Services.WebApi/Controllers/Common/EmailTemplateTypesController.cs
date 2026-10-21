using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.UseCases.Common.EmailTemplateTypes.Commands;
using JOIN.Application.UseCases.Common.EmailTemplateTypes.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// SPEC 71 — the catalog of email template classifications of the company. Deleting a type with live
/// templates answers <c>EMAIL_TEMPLATE_TYPE_IN_USE</c>; restoring one is reserved for SuperAdmin.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("EmailTemplateTypes")]
public class EmailTemplateTypesController(ISender sender) : ControllerBase
{
    /// <summary>The company's catalog of template types.</summary>
    [HttpGet]
    [RequirePermission(PermissionFlags.CanRead)]
    [ProducesResponseType(typeof(Response<IReadOnlyList<EmailTemplateTypeDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool? includeInactive = null,
        [FromQuery] bool? includeDeleted = null,
        CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateResult(await sender.Send(new GetEmailTemplateTypesQuery(includeInactive, includeDeleted), cancellationToken));

    /// <summary>Creates a classification.</summary>
    [HttpPost]
    [RequirePermission(PermissionFlags.CanCreate)]
    [ProducesResponseType(typeof(Response<EmailTemplateTypeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateEmailTemplateTypeCommand command, CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateCreated(await sender.Send(command, cancellationToken));

    /// <summary>Updates a classification.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<EmailTemplateTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmailTemplateTypeCommand command, CancellationToken cancellationToken = default)
    {
        var updated = command with { Id = id };
        return this.ToEmailTemplateResult(await sender.Send(updated, cancellationToken));
    }

    /// <summary>Logical delete of a classification: blocked when live templates use it.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionFlags.CanDelete)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateResult(await sender.Send(new DeleteEmailTemplateTypeCommand(id), cancellationToken));

    /// <summary>Restores a logically deleted classification. Restricted to the SuperAdmin role.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await sender.Send(new RestoreEmailTemplateTypeCommand(id, null), cancellationToken));
}
