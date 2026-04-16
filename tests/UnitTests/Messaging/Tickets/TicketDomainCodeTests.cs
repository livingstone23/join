using JOIN.Domain.Messaging;

namespace JOIN.UnitTests.Messaging.Tickets;

[TestClass]
public sealed class TicketDomainCodeTests
{
    [TestMethod]
    public void SetStandardCode_ShouldAssignExpectedFormat()
    {
        var ticket = new Ticket();

        ticket.SetStandardCode(2026, 4, 12);

        Assert.AreEqual("TICK-202604-0012", ticket.Code);
    }

    [TestMethod]
    public void SetPersonalizedCode_ShouldApplyConfiguredPadding()
    {
        var ticket = new Ticket();

        ticket.SetPersonalizedCode("JOIN", 25, 6);

        Assert.AreEqual("JOIN-000025", ticket.Code);
    }

    [TestMethod]
    public void SetPersonalizedCode_ShouldThrowWhenPrefixIsEmpty()
    {
        var ticket = new Ticket();

        Assert.ThrowsExactly<ArgumentException>(() => ticket.SetPersonalizedCode(" ", 1, 6));
    }
}