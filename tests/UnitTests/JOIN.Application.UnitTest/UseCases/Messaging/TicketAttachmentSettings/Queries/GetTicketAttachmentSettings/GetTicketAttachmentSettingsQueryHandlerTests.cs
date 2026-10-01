using AutoFixture;
using FluentAssertions;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Messaging.TicketAttachmentSettings.Queries;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.TicketAttachmentSettings.Queries.GetTicketAttachmentSettings;

/// <summary>
/// Unit tests for <see cref="GetTicketAttachmentSettingsQueryHandler"/> (list endpoint).
/// </summary>
public sealed class GetTicketAttachmentSettingsQueryHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenCompanyIdIsEmpty_ShouldReturnCompanyRequiredError()
    {
        var context = new GetListTestContext(companyId: Guid.Empty);
        var handler = context.CreateHandler();

        var response = await handler.Handle(new GetTicketAttachmentSettingsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("COMPANY_REQUIRED");
        context.ConnectionFactoryMock.Verify(x => x.CreateConnection(), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenNoConfigurationExists_ShouldReturnEmptyList()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetListTestContext(companyId);
        var handler = context.CreateHandler();

        context.Connection.SetResults(FakeResultSet.Empty(
            "Id", "CompanyId", "CompanyName", "GcRecord",
            "AllowedDocumentTypes", "MaxFileSizeBytes", "MaxFilesPerTicket", "MaxFilesPerDay", "CreatedAt"));

        var response = await handler.Handle(new GetTicketAttachmentSettingsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_WhenConfigurationExists_ShouldProjectDtoWithBitmaskFields()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new GetListTestContext(companyId);
        var handler = context.CreateHandler();

        context.Connection.SetResults(FakeResultSet.FromRows(
            new Dictionary<string, object?>
            {
                ["Id"] = Guid.NewGuid(),
                ["CompanyId"] = companyId,
                ["CompanyName"] = "JOIN",
                ["GcRecord"] = 0,
                ["AllowedDocumentTypes"] = 31,
                ["MaxFileSizeBytes"] = 10485760L,
                ["MaxFilesPerTicket"] = 10,
                ["MaxFilesPerDay"] = (int?)null,
                ["CreatedAt"] = DateTime.UtcNow
            }));

        var response = await handler.Handle(new GetTicketAttachmentSettingsQuery(), CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Count.Should().Be(1);
        var dto = response.Data.First();
        dto.AllowedDocumentTypeNames.Should().Contain("Pdf");
        dto.AllowedDocumentTypeNames.Should().Contain("Word");
        dto.AllowedDocumentTypeNames.Should().Contain("Excel");
        dto.AllowedDocumentTypeNames.Should().Contain("Text");
        dto.AllowedDocumentTypeNames.Should().Contain("Image");
        dto.AllowedDocumentTypeNames.Should().NotContain("Other");
        dto.MaxFileSizeBytes.Should().Be(10485760L);
        dto.MaxFilesPerDay.Should().BeNull();
    }

    private sealed class GetListTestContext
    {
        public GetListTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);
            ConnectionFactoryMock.Setup(x => x.CreateConnection()).Returns(Connection);
        }

        public Mock<ISqlConnectionFactory> ConnectionFactoryMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public FakeDbConnection Connection { get; } = new();

        public GetTicketAttachmentSettingsQueryHandler CreateHandler()
            => new(ConnectionFactoryMock.Object, CurrentUserServiceMock.Object);
    }
}