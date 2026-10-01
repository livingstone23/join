using AutoFixture;
using Dapper;
using FluentAssertions;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Queries.GetTicketAttachmentSettingById;

/// <summary>
/// Unit tests for <see cref="GetTicketAttachmentSettingByIdQueryHandler"/>.
/// Uses the existing FakeDapperInfrastructure so the test exercises the real
/// CommandDefinition / parameters path without needing SQL Server.
/// </summary>
public sealed class GetTicketAttachmentSettingByIdQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetByIdTestContext(companyId: Guid.Empty);
        var handler = context.CreateHandler();
        var query = new GetTicketAttachmentSettingByIdQuery(_fixture.Create<Guid>());

        var response = await handler.Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenConfigurationDoesNotExist_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetByIdTestContext(companyId);
        var handler = context.CreateHandler();
        var query = new GetTicketAttachmentSettingByIdQuery(_fixture.Create<Guid>());

        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "CompanyId", "CompanyName", "GcRecord",
            "AllowedDocumentTypes", "MaxFileSizeBytes", "MaxFilesPerTicket", "MaxFilesPerDay", "CreatedAt"));

        var response = await handler.Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_ATTACHMENT_SETTINGS_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenConfigurationExists_ShouldReturnDto()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetByIdTestContext(companyId);
        var handler = context.CreateHandler();
        var query = new GetTicketAttachmentSettingByIdQuery(_fixture.Create<Guid>());

        context.Connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = query.Id,
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN",
                ["GcRecord"] = 0,
                ["AllowedDocumentTypes"] = 31,
                ["MaxFileSizeBytes"] = 10485760L,
                ["MaxFilesPerTicket"] = 10,
                ["MaxFilesPerDay"] = (int?)null,
                ["CreatedAt"] = DateTime.UtcNow
            }));

        var response = await handler.Handle(query, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Id.Should().Be(query.Id);
        response.Data.CompanyId.Should().Be(companyId);
        response.Data.MaxFilesPerDay.Should().BeNull();
        response.Data.AllowedDocumentTypeNames.Should().Contain("Pdf");
    }

    private sealed class GetByIdTestContext
    {
        public GetByIdTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketAttachmentSettingByIdQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}