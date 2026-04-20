using FluentAssertions;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.UnitTest.UseCases.Messaging.Tickets.Queries.TestDoubles;
using JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;
using Moq;

namespace JOIN.Application.UnitTest.UseCases.Security.Queries.GetSystemWideUserReport;

/// <summary>
/// Contains pure-logic unit tests for the <c>UserManagementReportQueryHelper</c> class (internal).
/// Because the helper is internal to <c>JOIN.Application</c> and its methods are private, all
/// scenarios are exercised indirectly through <see cref="GetSystemWideUserReportQueryHandler"/>.
///
/// NormalizeDateRange — covers null inputs, valid ranges, same-day boundaries, inverted dates,
/// and truncation of time-of-day components.
///
/// BuildFullName — covers all name/email combinations, whitespace trimming, and the empty-string
/// fallback when no identifying information is available.
/// </summary>
public sealed class UserManagementReportQueryHelperTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a fake-connection row that satisfies all columns expected by
    /// <c>UserManagementReportSqlRow</c>.
    /// </summary>
    private static IDictionary<string, object?> MakeUserRow(
        string? firstName,
        string? lastName,
        string? email = "user@example.com")
    {
        return new Dictionary<string, object?>
        {
            ["UserId"] = Guid.NewGuid(),
            ["FirstName"] = firstName,
            ["LastName"] = lastName,
            ["Email"] = email,
            ["IsActive"] = true,
            ["UserCreatedDate"] = DateTime.UtcNow.Date,
            ["LastLoginDate"] = null,
            ["CompanyId"] = null,
            ["CompanyName"] = null,
            ["IsDefaultCompany"] = null,
            ["RoleName"] = null
        };
    }

    private static GetSystemWideUserReportQueryHandler CreateHandler(FakeDbConnection fakeConnection)
    {
        var factoryMock = new Mock<ISqlConnectionFactory>();
        factoryMock.Setup(x => x.CreateConnection()).Returns(fakeConnection);
        return new GetSystemWideUserReportQueryHandler(factoryMock.Object);
    }

    // ── NormalizeDateRange — null and valid paths ─────────────────────────────

    /// <summary>
    /// Verifies that providing no dates at all does not throw and returns an empty collection.
    /// </summary>
    [Fact]
    public async Task Handle_WhenBothDatesAreNull_ShouldSucceedAndReturnEmptyList()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.Empty(
            "UserId", "FirstName", "LastName", "Email", "IsActive",
            "UserCreatedDate", "LastLoginDate", "CompanyId", "CompanyName",
            "IsDefaultCompany", "RoleName"));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(FromDate: null, ToDate: null),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a valid date range (FromDate strictly before ToDate) does not throw.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFromDateIsBeforeToDate_ShouldSucceed()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.Empty(
            "UserId", "FirstName", "LastName", "Email", "IsActive",
            "UserCreatedDate", "LastLoginDate", "CompanyId", "CompanyName",
            "IsDefaultCompany", "RoleName"));

        var handler = CreateHandler(fakeConnection);

        // Act
        Func<Task> act = () => handler.Handle(
            new GetSystemWideUserReportQuery(
                FromDate: new DateTime(2025, 1, 1),
                ToDate: new DateTime(2025, 12, 31)),
            CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Verifies that equal FromDate and ToDate values are treated as a valid single-day window
    /// (because ToDate is internally converted to an exclusive upper bound by adding one day).
    /// </summary>
    [Fact]
    public async Task Handle_WhenFromDateEqualsToDate_ShouldSucceed()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.Empty(
            "UserId", "FirstName", "LastName", "Email", "IsActive",
            "UserCreatedDate", "LastLoginDate", "CompanyId", "CompanyName",
            "IsDefaultCompany", "RoleName"));

        var handler = CreateHandler(fakeConnection);

        // Act
        Func<Task> act = () => handler.Handle(
            new GetSystemWideUserReportQuery(
                FromDate: new DateTime(2025, 6, 15),
                ToDate: new DateTime(2025, 6, 15)),
            CancellationToken.None);

        // Assert — Jan 15 < Jan 16 (exclusive), so no exception should be raised
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Verifies that an inverted date range (FromDate after ToDate) raises a
    /// <see cref="ValidationException"/> with the canonical error message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenFromDateIsAfterToDate_ShouldThrowValidationException()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        var handler = CreateHandler(fakeConnection);

        // Act
        Func<Task> act = () => handler.Handle(
            new GetSystemWideUserReportQuery(
                FromDate: new DateTime(2025, 6, 20),
                ToDate: new DateTime(2025, 6, 15)),
            CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<JOIN.Application.Common.ValidationException>();
        ex.Which.Errors.Should().ContainKey("ToDate")
            .WhoseValue.Should().Contain("'ToDate' must be greater than or equal to 'FromDate'.");
    }

    /// <summary>
    /// Verifies that any time-of-day component on the input dates is stripped;
    /// after truncation a same-day range must still be accepted.
    /// </summary>
    [Fact]
    public async Task Handle_WhenDatesContainTimeParts_ShouldTruncateToDateOnly()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.Empty(
            "UserId", "FirstName", "LastName", "Email", "IsActive",
            "UserCreatedDate", "LastLoginDate", "CompanyId", "CompanyName",
            "IsDefaultCompany", "RoleName"));

        var handler = CreateHandler(fakeConnection);

        // Act — Jan 15 at 23:59:59 as FromDate and Jan 15 at 00:00:01 as ToDate:
        //       after truncation both become Jan 15 00:00:00, which is a valid same-day range.
        Func<Task> act = () => handler.Handle(
            new GetSystemWideUserReportQuery(
                FromDate: new DateTime(2025, 1, 15, 23, 59, 59),
                ToDate: new DateTime(2025, 1, 15, 0, 0, 1)),
            CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    // ── BuildFullName — FullName mapping on returned DTO ────────────────────

    /// <summary>
    /// Verifies that a non-null FirstName and LastName are combined with a single space.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasBothFirstAndLastName_FullNameShouldBeFirstPlusLast()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeUserRow("John", "Doe")));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.Data!.Single().FullName.Should().Be("John Doe");
    }

    /// <summary>
    /// Verifies that a user with a first name but no last name gets a FullName of just the first name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasOnlyFirstName_FullNameShouldBeFirstNameOnly()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeUserRow("Alice", null)));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.Data!.Single().FullName.Should().Be("Alice");
    }

    /// <summary>
    /// Verifies that a user with only a last name gets a FullName of just the last name.
    /// </summary>
    [Fact]
    public async Task Handle_WhenUserHasOnlyLastName_FullNameShouldBeLastNameOnly()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeUserRow(null, "Smith")));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.Data!.Single().FullName.Should().Be("Smith");
    }

    /// <summary>
    /// Verifies that when both names are null the FullName falls back to the user's email address.
    /// </summary>
    [Fact]
    public async Task Handle_WhenBothNamesAreNull_FullNameShouldFallBackToEmail()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeUserRow(null, null, email: "fallback@example.com")));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.Data!.Single().FullName.Should().Be("fallback@example.com");
    }

    /// <summary>
    /// Verifies that whitespace-only name parts are excluded and the email is used as fallback.
    /// </summary>
    [Fact]
    public async Task Handle_WhenBothNamesAreWhitespace_FullNameShouldFallBackToEmail()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeUserRow("   ", "  \t  ", email: "ws@example.com")));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.Data!.Single().FullName.Should().Be("ws@example.com");
    }

    /// <summary>
    /// Verifies that when both names are null or whitespace and the email is also null,
    /// the FullName is an empty string.
    /// </summary>
    [Fact]
    public async Task Handle_WhenBothNamesAndEmailAreNull_FullNameShouldBeEmptyString()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeUserRow(null, null, email: null)));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.Data!.Single().FullName.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that leading and trailing whitespace is trimmed from both first and last names
    /// before they are concatenated.
    /// </summary>
    [Fact]
    public async Task Handle_WhenNamesHaveExtraWhitespace_FullNameShouldBeTrimmed()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.FromRows(
            MakeUserRow("  Jane  ", "  Doe  ")));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.Data!.Single().FullName.Should().Be("Jane Doe");
    }

    // ── handler contract ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the success response carries the expected message.
    /// </summary>
    [Fact]
    public async Task Handle_WhenSuccessful_ShouldReturnExpectedMessage()
    {
        // Arrange
        var fakeConnection = new FakeDbConnection();
        fakeConnection.SetResults(FakeResultSet.Empty(
            "UserId", "FirstName", "LastName", "Email", "IsActive",
            "UserCreatedDate", "LastLoginDate", "CompanyId", "CompanyName",
            "IsDefaultCompany", "RoleName"));

        var handler = CreateHandler(fakeConnection);

        // Act
        var result = await handler.Handle(
            new GetSystemWideUserReportQuery(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("System-wide user report retrieved successfully.");
    }
}
