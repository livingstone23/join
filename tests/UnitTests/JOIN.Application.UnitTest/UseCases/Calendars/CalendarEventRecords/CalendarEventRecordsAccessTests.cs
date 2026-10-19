using FluentAssertions;

namespace JOIN.Application.UnitTest.UseCases.Calendars.CalendarEventRecords;

/// <summary>
/// SPEC 69 rule 5 — who sees the comments and documents of an activity: the owner, the
/// <c>CanViewOthersActivities</c> flag (SPEC 68), the Panel's <c>CalendarOthers.CanRead</c> or a SuperAdmin.
/// Anyone else gets the same not-found verdict as a missing activity.
/// </summary>
public sealed class CalendarEventRecordsAccessTests
{
    [Fact]
    public async Task ResolveAsync_WhenOwner_IsVisible()
    {
        var context = new Spec69CalendarEventRecordsContext();

        var result = await context.Access.ResolveAsync(context.EventId, CancellationToken.None);

        result.IsFoundAndVisible.Should().BeTrue();
        result.CalendarEvent.Should().BeSameAs(context.CalendarEvent);
    }

    [Fact]
    public async Task ResolveAsync_WhenActivityIsMissing_IsNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext(ownerCanSeeEvent: false);

        var result = await context.Access.ResolveAsync(context.EventId, CancellationToken.None);

        result.IsFoundAndVisible.Should().BeFalse();
        result.CalendarEvent.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_WhenForeignWithoutFlagOrPanel_IsNotFound()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.MakeEventForeign();

        var result = await context.Access.ResolveAsync(context.EventId, CancellationToken.None);

        result.IsFoundAndVisible.Should().BeFalse();
        result.CalendarEvent.Should().BeNull("a hidden activity must look exactly like a missing one");
    }

    [Fact]
    public async Task ResolveAsync_WhenForeignWithViewOthersFlag_IsVisible()
    {
        var context = new Spec69CalendarEventRecordsContext(canViewOthers: true);
        context.MakeEventForeign();

        var result = await context.Access.ResolveAsync(context.EventId, CancellationToken.None);

        result.IsFoundAndVisible.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_WhenForeignWithCalendarOthersRead_IsVisible()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.MakeEventForeign();
        context.GrantCalendarOthersRead();

        var result = await context.Access.ResolveAsync(context.EventId, CancellationToken.None);

        result.IsFoundAndVisible.Should().BeTrue();
        result.CurrentUserId.Should().Be(context.ActorUserId);
    }

    [Fact]
    public async Task ResolveAsync_WhenForeignAndSuperAdmin_IsVisible()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.MakeEventForeign();
        context.CurrentUserServiceMock.Setup(x => x.IsInRole("SuperAdmin")).Returns(true);

        var result = await context.Access.ResolveAsync(context.EventId, CancellationToken.None);

        result.IsFoundAndVisible.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_WhenActivityOfAnotherCompany_IsNotFoundEvenWithPanel()
    {
        var context = new Spec69CalendarEventRecordsContext();
        context.CalendarEvent.CompanyId = Guid.NewGuid();
        context.GrantCalendarOthersRead();

        var result = await context.Access.ResolveAsync(context.EventId, CancellationToken.None);

        result.IsFoundAndVisible.Should().BeFalse();
    }
}
