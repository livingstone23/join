namespace JOIN.Application.DTO.Security.User;

/// <summary>Result of an invitation request. See SPEC 27 item 15 for the decision tree.</summary>
public enum InviteOutcome
{
    /// <summary>New user created without password + invitation email sent.</summary>
    Created = 1,

    /// <summary>Existing user added to the calling tenant + roles linked + notice email sent.</summary>
    MembershipAdded = 2,

    /// <summary>Pending invitation re-sent with a fresh token; existing roles replaced.</summary>
    InvitationResent = 3
}
