using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PaGetto.Core.Email;

/// <summary>
/// An <see cref="IEmailSender"/> that drops messages. Used when email is not
/// configured (<c>Email:Type</c> is unset or <c>Null</c>) so that callers can
/// always resolve an <see cref="IEmailSender"/>.
/// </summary>
public partial class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        LogMessageDropped(message.To, message.Subject);

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Email sending is disabled; dropping message to {Recipients} with subject {Subject}.")]
    private partial void LogMessageDropped(IReadOnlyList<string> recipients, string subject);
}
