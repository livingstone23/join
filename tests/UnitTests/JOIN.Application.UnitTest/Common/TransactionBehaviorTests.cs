using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using MediatR;
using Moq;

namespace JOIN.Application.UnitTest.Common;

/// <summary>
/// Unit tests for <see cref="TransactionBehavior{TRequest, TResponse}"/> covering the four
/// branches of the pipeline: non-transactional passthrough, success commit,
/// business-failure rollback, and exception rollback. A fifth case guards the legacy
/// commit-on-exit behavior when TResponse does not expose <c>IsSuccess</c>.
/// </summary>
public sealed class TransactionBehaviorTests
{
    // ──────────────────────────────────────────────
    //  Test request shapes
    // ──────────────────────────────────────────────

    /// <summary>Plain MediatR request — never touches the UnitOfWork.</summary>
    public sealed record PlainRequest(string Value) : IRequest<Response<string>>;

    /// <summary>Transactional command — wraps a Response&lt;T&gt;.</summary>
    public sealed record TransactionalRequest(string Value) : ITransactionalCommand<Response<string>>;

    /// <summary>Transactional command with a non-Response TResponse — guards the
    /// commit-on-exit behavior preserved for responses that do not expose IsSuccess.</summary>
    public sealed record TransactionalPrimitiveRequest(string Value) : ITransactionalCommand<string>;

    // ──────────────────────────────────────────────
    //  1. Non-transactional request — passthrough
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenRequestIsNotTransactional_ShouldNotTouchUnitOfWork()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var behavior = new TransactionBehavior<PlainRequest, Response<string>>(uow.Object);
        var nextCalled = false;

        var response = await behavior.Handle(
            new PlainRequest("hello"),
            _ =>
            {
                nextCalled = true;
                return Task.FromResult(new Response<string> { IsSuccess = true, Data = "ok" });
            },
            CancellationToken.None);

        nextCalled.Should().BeTrue();
        response.IsSuccess.Should().BeTrue();
        response.Data.Should().Be("ok");
        uow.VerifyNoOtherCalls();
    }

    // ──────────────────────────────────────────────
    //  2. Transactional + IsSuccess = true → commit
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenTransactionalAndResponseIsSuccess_ShouldCommit()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var behavior = new TransactionBehavior<TransactionalRequest, Response<string>>(uow.Object);

        var response = await behavior.Handle(
            new TransactionalRequest("hello"),
            _ => Task.FromResult(new Response<string> { IsSuccess = true, Data = "ok" }),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ──────────────────────────────────────────────
    //  3. Transactional + IsSuccess = false → rollback (no throw)
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenTransactionalAndResponseIsFailure_ShouldRollbackAndReturnResponse()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var behavior = new TransactionBehavior<TransactionalRequest, Response<string>>(uow.Object);

        var failedResponse = Response<string>.Error("BOOM", ["detail"]);
        var response = await behavior.Handle(
            new TransactionalRequest("hello"),
            _ => Task.FromResult(failedResponse),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.Message.Should().Be("BOOM");
        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ──────────────────────────────────────────────
    //  4. Transactional + exception → rollback + rethrow
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenTransactionalAndNextThrows_ShouldRollbackAndRethrow()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var behavior = new TransactionBehavior<TransactionalRequest, Response<string>>(uow.Object);

        Func<Task> act = () => behavior.Handle(
            new TransactionalRequest("hello"),
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ──────────────────────────────────────────────
    //  5. Transactional + non-Response TResponse → keeps legacy commit behavior
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Handle_WhenTransactionalButResponseHasNoIsSuccess_ShouldCommit()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var behavior = new TransactionBehavior<TransactionalPrimitiveRequest, string>(uow.Object);

        var response = await behavior.Handle(
            new TransactionalPrimitiveRequest("hello"),
            _ => Task.FromResult("ok"),
            CancellationToken.None);

        response.Should().Be("ok");
        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
