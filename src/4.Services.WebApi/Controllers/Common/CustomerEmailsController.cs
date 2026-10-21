using Asp.Versioning;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common.Emails;
using JOIN.Application.UseCases.Common.Emails.Commands;
using JOIN.Application.UseCases.Common.Emails.Queries;
using JOIN.Domain.Security;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.Services.WebApi.Controllers.Common;

/// <summary>
/// SPEC 72 — the emails of a customer, under the <c>Customers</c> resource: they go to the person of the customer with
/// <c>Origin = Customer</c>, and the history is the one of that person.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/Customers/{customerId:guid}/emails")]
[Produces("application/json")]
[PermissionResource("Customers")]
public class CustomerEmailsController(IMediator mediator) : ControllerBase
{
    /// <summary>Sends an email to the person of the customer; the lead is optional.</summary>
    [HttpPost]
    [RequirePermission(PermissionFlags.CanExecute)]
    [ProducesResponseType(typeof(Response<PersonEmailSentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(Response<PersonEmailSentDto>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Send(Guid customerId, [FromBody] SendCustomerEmailCommand command, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailCreated(await mediator.Send(command with { CustomerId = customerId }, cancellationToken));

    /// <summary>The email history of the person of the customer, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(Response<PagedResult<PersonEmailMessageListItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(Guid customerId, [FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20, CancellationToken cancellationToken = default) =>
        this.ToPersonEmailResult(await mediator.Send(new GetPersonEmailsQuery(PersonEmailHistoryScope.Customer, customerId, pageNumber, pageSize), cancellationToken));
}
