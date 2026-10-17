using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.Leads;
using JOIN.Application.UseCases.Customers.Leads.Documents;
using JOIN.Application.UseCases.Messaging.Tickets;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Customers.Leads;

/// <summary>
/// Documents of a lead and its contacts (SPEC 62), stored with the configured provider (SPEC 49-post). Only open leads
/// accept new files; any visible lead lists and downloads them.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/leads/{leadId:guid}/documents")]
[Produces("application/json")]
[PermissionResource("Leads")]
public class LeadDocumentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<IReadOnlyList<LeadDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid leadId,
        [FromQuery] Guid? leadInteractionId = null,
        [FromQuery] bool? includeDeleted = null,
        CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new GetLeadDocumentsQuery(leadId, leadInteractionId, includeDeleted), cancellationToken));

    [HttpGet("{id:guid}/download")]
    [RequirePermission(PermissionFlags.CanDownload)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Download(Guid leadId, Guid id, CancellationToken cancellationToken = default)
    {
        var response = await mediator.Send(new DownloadLeadDocumentQuery(leadId, id), cancellationToken);
        return response.IsSuccess
            ? File(response.Data!.Content, response.Data.ContentType, response.Data.OriginalName)
            : this.ToCommercialResult(response);
    }

    /// <summary>Multipart upload: <c>file</c> and optional <c>leadInteractionId</c>.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequirePermission(PermissionFlags.CanUpdate)]
    [ProducesResponseType(typeof(Response<LeadDocumentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Upload(Guid leadId, [FromForm] IFormFile file, [FromForm] Guid? leadInteractionId, CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            return BadRequest(Response<object>.Error("INVALID_FILE", ["The 'file' form field is required."]));
        }

        var attachment = new InboundAttachment(file.OpenReadStream(), file.FileName, file.ContentType ?? "application/octet-stream", file.Length);
        var response = await mediator.Send(new UploadLeadDocumentCommand { LeadId = leadId, LeadInteractionId = leadInteractionId, File = attachment }, cancellationToken);
        return this.ToCommercialCreated(response);
    }

    /// <summary>Logical delete; the file is kept and the row can be restored.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid leadId, Guid id, CancellationToken cancellationToken = default) =>
        this.ToCommercialResult(await mediator.Send(new DeleteLeadDocumentCommand(leadId, id), cancellationToken));

    [HttpPost("{id:guid}/restore")]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<Guid>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid leadId, Guid id, [FromQuery] Guid? companyId = null, CancellationToken cancellationToken = default) =>
        this.ToRestoreResult(await mediator.Send(new RestoreLeadDocumentCommand(leadId, id, companyId), cancellationToken));
}
