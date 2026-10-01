using AutoFixture;
using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Messaging.Tickets.Commands.DeleteTicketDocument;
using JOIN.Domain.Enums;
using JOIN.Domain.Messaging;
using JOIN.Domain.Support;
using MediatR;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Commands.DeleteTicketDocument;

/// <summary>
/// Unit tests for <see cref="DeleteTicketDocumentCommandHandler"/>. Verifies
/// that the handler soft-deletes the metadata row only — the storage
/// service is NEVER invoked (the binario se queda como evidencia).
/// </summary>
public sealed class DeleteTicketDocumentCommandHandlerTests
{
    private readonly Fixture _fixture = new();

    [Fact]
    public async Task Handle_WhenDocumentDoesNotExist_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteDocTestContext(companyId);
        var handler = context.CreateHandler();

        var request = new DeleteTicketDocumentCommand { TicketId = _fixture.Create<Guid>(), Id = _fixture.Create<Guid>() };
        context.DocumentRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync((TicketDocument?)null);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_DOCUMENT_NOT_FOUND");
        context.UnitOfWorkMock.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenDocumentBelongsToDifferentTenant_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteDocTestContext(companyId);
        var handler = context.CreateHandler();

        var request = new DeleteTicketDocumentCommand { TicketId = _fixture.Create<Guid>(), Id = _fixture.Create<Guid>() };
        var doc = CreateDocument(request.Id, request.TicketId, _fixture.Create<Guid>());
        context.DocumentRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(doc);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_DOCUMENT_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenDocumentBelongsToDifferentTicketOfSameTenant_ShouldReturnNotFoundError()
    {
        var companyId = _fixture.Create<Guid>();
        var context = new DeleteDocTestContext(companyId);
        var handler = context.CreateHandler();

        var request = new DeleteTicketDocumentCommand { TicketId = _fixture.Create<Guid>(), Id = _fixture.Create<Guid>() };
        var doc = CreateDocument(request.Id, _fixture.Create<Guid>(), companyId);
        context.DocumentRepositoryMock.Setup(x => x.GetAsync(request.Id)).ReturnsAsync(doc);

        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("TICKET_DOCUMENT_NOT_FOUND");
    }

    [Fact]
    public async Task Handle_WhenDocumentExistsForCurrentTicketAndTenant_ShouldSoftDeleteWithoutInvokingStorageDelete()
    {
        var companyId = _fixture.Create<Guid>();
        var ticketId = _fixture.Create<Guid>();
        var docId = _fixture.Create<Guid>();

        var context = new DeleteDocTestContext(companyId);
        // The handler doesn't take IFileStorageService — we assert that
        // DeleteAsync is NEVER called via the storage interface, asserting
        // the spec decision "soft delete of metadata without erasing the binary".
        var handler = context.CreateHandler();

        var doc = CreateDocument(docId, ticketId, companyId);
        SetEntityId(doc, docId);
        context.DocumentRepositoryMock.Setup(x => x.GetAsync(docId)).ReturnsAsync(doc);
        context.DocumentRepositoryMock.Setup(x => x.UpdateAsync(It.IsAny<TicketDocument>())).ReturnsAsync(true);
        context.UnitOfWorkMock.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var request = new DeleteTicketDocumentCommand { TicketId = ticketId, Id = docId };
        var response = await handler.Handle(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be(docId);
        doc.GcRecord.Should().NotBe(0);
        context.FileStorageServiceMock.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static TicketDocument CreateDocument(Guid id, Guid ticketId, Guid companyId) => new()
    {
        TicketId = ticketId,
        CompanyId = companyId,
        TicketLogsId = Guid.NewGuid(),
        DocumentType = DocumentType.Pdf,
        OriginalName = "r.pdf",
        NewName = "x.pdf",
        Path = $"{companyId}/{ticketId}/x.pdf",
        StorageProvider = StorageProviderKind.Local,
        ContentType = "application/pdf",
        SizeBytes = 100,
        GcRecord = 0
    };

    private static void SetEntityId(JOIN.Domain.Audit.BaseEntity entity, Guid id)
        => typeof(JOIN.Domain.Audit.BaseEntity).GetProperty(nameof(JOIN.Domain.Audit.BaseEntity.Id))!.SetValue(entity, id);

    private sealed class DeleteDocTestContext
    {
        public DeleteDocTestContext(Guid companyId)
        {
            CurrentUserServiceMock.SetupGet(x => x.CompanyId).Returns(companyId);
            CurrentUserServiceMock.SetupGet(x => x.UserId).Returns(Guid.NewGuid().ToString());
            CurrentUserServiceMock.SetupGet(x => x.IsAuthenticated).Returns(true);

            UnitOfWorkMock.Setup(x => x.GetRepository<TicketDocument>()).Returns(DocumentRepositoryMock.Object);
        }

        public Mock<IUnitOfWork> UnitOfWorkMock { get; } = new();
        public Mock<ICurrentUserService> CurrentUserServiceMock { get; } = new();
        public Mock<IGenericRepository<TicketDocument>> DocumentRepositoryMock { get; } = new();
        public Mock<IFileStorageService> FileStorageServiceMock { get; } = new();

        public IRequestHandler<DeleteTicketDocumentCommand, Response<Guid>> CreateHandler()
            => new DeleteTicketDocumentCommandHandler(UnitOfWorkMock.Object, CurrentUserServiceMock.Object);
    }
}