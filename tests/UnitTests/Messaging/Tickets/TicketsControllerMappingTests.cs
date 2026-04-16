using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.UseCases.Messaging.Tickets.Commands;
using JOIN.Services.WebApi.Controllers.Messaging;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace JOIN.UnitTests.Messaging.Tickets;

[TestClass]
public sealed class TicketsControllerMappingTests
{
    [TestMethod]
    public async Task Create_ShouldForwardEffortPointsToCommand()
    {
        var sender = new CapturingSender();
        var controller = new TicketsController(sender);
        var dto = new CreateTicketDto
        {
            Name = "Ticket test",
            Description = "Controller mapping test",
            EstimatedTime = 8,
            ConsumedTime = 0,
            EffortPoints = 3,
            IsVisibleToExternals = true,
            TicketStatusId = Guid.NewGuid(),
            TicketComplexityId = Guid.NewGuid(),
            TimeUnitId = Guid.NewGuid(),
            ChannelId = Guid.NewGuid()
        };

        await controller.Create(dto, CancellationToken.None);

        Assert.IsNotNull(sender.LastCreateCommand);
        Assert.AreEqual(3m, sender.LastCreateCommand!.EffortPoints);
    }

    private sealed class CapturingSender : ISender
    {
        public CreateTicketCommand? LastCreateCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is CreateTicketCommand createCommand)
            {
                LastCreateCommand = createCommand;

                var response = new Response<TicketDto>
                {
                    IsSuccess = true,
                    Data = new TicketDto { Id = Guid.NewGuid() }
                };

                return Task.FromResult((TResponse)(object)response);
            }

            throw new NotSupportedException("Only CreateTicketCommand is supported in this test.");
        }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken, bool _) =>
            Send(request, cancellationToken);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            if (request is CreateTicketCommand createCommand)
            {
                LastCreateCommand = createCommand;
            }

            return Task.CompletedTask;
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}