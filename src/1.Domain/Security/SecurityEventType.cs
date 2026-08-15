namespace JOIN.Domain.Security;

/// <summary>
/// Catalogue of security-relevant events captured by the audit trail.
/// Stored as <c>int</c> in <c>Security.SecurityEventLogs.EventType</c>.
/// </summary>
public enum SecurityEventType
{
    LoginSucceeded = 1,
    LoginFailed = 2,
    LoginMfaRequired = 3,
    PasswordChanged = 4,
    EmailChangeRequested = 5,
    EmailChangeConfirmed = 6,
    EmailChangeFailed = 7,
    MfaSetupInitiated = 8,
    MfaEnabled = 9,
    MfaDisabled = 10,
    MfaRecoveryCodeUsed = 11,
    PhoneVerificationRequested = 12,
    PhoneVerificationConfirmed = 13,
    PhoneVerificationFailed = 14,
    PhoneVerificationLocked = 15,
    SessionRevoked = 16,
    SessionsRevokedOthers = 17,
    RoleChanged = 18,
}
