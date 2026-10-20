using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.EmailTemplates;
using JOIN.Application.UseCases.Common.EmailTemplates;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Controllers.Common;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// SPEC 71 — the email templates of the company. The administrative endpoints need the
/// <c>EmailTemplates</c> resource; the picker of the sending modal (<c>lookup</c>), the list of
/// variables and the render are open to any authenticated user of the company
/// (<see cref="SkipDynamicAuthorizationAttribute"/>), because the seller sends the mail without
/// administering the templates.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("EmailTemplates")]
public class EmailTemplatesController(ISender sender) : ControllerBase
{
    /// <summary>The company's templates, paged and filtered (search, type and active state).</summary>
    [HttpGet]
    [RequirePermission(PermissionFlags.CanRead)]
    [ProducesResponseType(typeof(Response<PagedResult<EmailTemplateDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPaged(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] Guid? emailTemplateTypeId = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var query = new GetEmailTemplatesQuery(search, emailTemplateTypeId, isActive, pageNumber, pageSize);
        return this.ToEmailTemplateResult(await sender.Send(query, cancellationToken));
    }

    /// <summary>The detail of one template.</summary>
    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionFlags.CanRead)]
    [ProducesResponseType(typeof(Response<EmailTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateResult(await sender.Send(new GetEmailTemplateByIdQuery(id), cancellationToken));

    /// <summary>The closed list of variables with its description, for the editor.</summary>
    [HttpGet("variables")]
    [SkipDynamicAuthorization]
    [ProducesResponseType(typeof(Response<IReadOnlyList<EmailTemplateVariableDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVariables(CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateResult(await sender.Send(new GetEmailTemplateVariablesQuery(), cancellationToken));

    /// <summary>The active templates of the company for the picker of the sending modal (max. 50).</summary>
    [HttpGet("lookup")]
    [SkipDynamicAuthorization]
    [ProducesResponseType(typeof(Response<IReadOnlyList<EmailTemplateLookupItemDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup(
        [FromQuery] Guid? emailTemplateTypeId = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateResult(await sender.Send(new LookupEmailTemplatesQuery(emailTemplateTypeId, search), cancellationToken));

    /// <summary>Creates a template: the type must be active and the variables must be known.</summary>
    [HttpPost]
    [RequirePermission(PermissionFlags.CanCreate)]
    [ProducesResponseType(typeof(Response<EmailTemplateDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateEmailTemplateCommand command, CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateCreated(await sender.Send(command, cancellationToken));

    /// <summary>Updates a template.</summary>
    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<EmailTemplateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEmailTemplateCommand command, CancellationToken cancellationToken = default)
    {
        var updated = command with { Id = id };
        return this.ToEmailTemplateResult(await sender.Send(updated, cancellationToken));
    }

    /// <summary>Logical delete of a template (SPEC 41-post).</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(PermissionFlags.CanDelete)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default) =>
        this.ToEmailTemplateResult(await sender.Send(new DeleteEmailTemplateCommand(id), cancellationToken));

    /// <summary>Restores a logically deleted template. Restricted to the SuperAdmin role.</summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await sender.Send(new RestoreEmailTemplateCommand(id, null), cancellationToken));

    /// <summary>
    /// The subject and the body of the template with the variables replaced by the values of the
    /// person, the lead, the user and the company.
    /// </summary>
    [HttpPost("{id:guid}/render")]
    [SkipDynamicAuthorization]
    [ProducesResponseType(typeof(Response<EmailTemplateRenderedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Render(Guid id, [FromBody] RenderEmailTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var query = new RenderEmailTemplateQuery(id, request.PersonId, request.EmailAddress, request.LeadId);
        return this.ToEmailTemplateResult(await sender.Send(query, cancellationToken));
    }
}

/// <summary>Payload of <c>POST /EmailTemplates/{id}/render</c> (SPEC 71).</summary>
public sealed record RenderEmailTemplateRequest(Guid PersonId, string? EmailAddress = null, Guid? LeadId = null);
