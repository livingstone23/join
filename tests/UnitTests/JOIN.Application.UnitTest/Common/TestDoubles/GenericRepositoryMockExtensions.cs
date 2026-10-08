using System.Linq.Expressions;
using JOIN.Application.Interface.Persistence;
using Moq;

namespace JOIN.Application.UnitTest.Common.TestDoubles;

/// <summary>
/// Moq helpers for <see cref="IGenericRepository{T}"/>.
/// </summary>
public static class GenericRepositoryMockExtensions
{
    /// <summary>
    /// Emulates <see cref="IGenericRepository{T}.GetAllIncludingDeletedAsync"/> over an in-memory table:
    /// the handler's predicate (translated to SQL in production) is compiled and applied to
    /// <paramref name="rows"/>, so tests exercise the real filter, including its <c>GcRecord</c> clause.
    /// </summary>
    public static void SetupRows<T>(this Mock<IGenericRepository<T>> repositoryMock, IEnumerable<T> rows)
        where T : class
    {
        var table = rows.ToList();
        repositoryMock
            .Setup(x => x.GetAllIncludingDeletedAsync(It.IsAny<Expression<Func<T, bool>>>()))
            .ReturnsAsync((Expression<Func<T, bool>> predicate) => table.Where(predicate.Compile()).ToList());
    }

    /// <summary>
    /// Registers a repository for <typeparamref name="T"/> on the unit of work whose
    /// <see cref="IGenericRepository{T}.GetAllIncludingDeletedAsync"/> serves <paramref name="rows"/>
    /// (see <see cref="SetupRows{T}"/>).
    /// </summary>
    public static Mock<IGenericRepository<T>> SetupRepositoryRows<T>(this Mock<IUnitOfWork> unitOfWorkMock, IEnumerable<T> rows)
        where T : class
    {
        var repositoryMock = new Mock<IGenericRepository<T>>();
        repositoryMock.SetupRows(rows);
        unitOfWorkMock.Setup(x => x.GetRepository<T>()).Returns(repositoryMock.Object);
        return repositoryMock;
    }
}
