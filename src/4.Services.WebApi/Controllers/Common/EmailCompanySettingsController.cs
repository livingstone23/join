using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.UseCases.Common.Emails.Commands;
using JOIN.Application.UseCases.Common.Emails.Queries;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// SPEC 72 — the sending rules of the token's company (sender, daily quota per user and total size of attachments),
/// under the <c>EmailSettings</c> resource. The sender of the company must be verified in SendGrid.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
[PermissionResource("EmailSettings")]
public class EmailCompanySettingsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(Response<EmailCompanySettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetEmailCompanySettingsQuery(), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(Response<EmailCompanySettingsDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateEmailCompanySettingsCommand command, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailCreated(await mediator.Send(command, cancellationToken));

    [HttpPut]
    [ProducesResponseType(typeof(Response<EmailCompanySettingsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateEmailCompanySettingsCommand command, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(command, cancellationToken));
}
