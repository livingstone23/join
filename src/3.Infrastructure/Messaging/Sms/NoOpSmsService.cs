// Copyright (c) 2026-2027 JOIN Inc. All rights reserved.
// See LICENSE in the project root for license information.

using JOIN.Application.Interface;
using Microsoft.Extensions.Logging;

namespace JOIN.Infrastructure.Messaging.Sms;

/// <summary>
/// Default <see cref="ISmsService"/> implementation. Logs the outgoing message body and
/// always reports success — Twilio lands in SPEC 30 and replaces this stub.
/// </summary>
public sealed class NoOpSmsService(ILogger<NoOpSmsService> logger) : ISmsService
{
    private readonly ILogger<NoOpSmsService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task<bool> SendSmsAsync(string phoneNumber, string message, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[NoOpSmsService] Would dispatch SMS to {PhoneNumber}: {Message}",
            phoneNumber,
            message);

        return Task.FromResult(true);
    }
}
