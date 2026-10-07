using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SmartStock.Application.Abstractions;

namespace SmartStock.Tests.Infrastructure;

public sealed partial class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public IReadOnlyList<EmailMessage> Sent => _sent.ToList();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public int CountFor(string email) => _sent.Count(m => m.To == email);

    public PasswordLink LastLinkFor(string email)
    {
        var message = _sent.LastOrDefault(m => m.To == email)
            ?? throw new InvalidOperationException($"Nenhum e-mail enviado para {email}.");

        var match = LinkRegex().Match(message.TextBody);
        if (!match.Success)
            throw new InvalidOperationException("O e-mail não contém link de senha.");

        return new PasswordLink(
            match.Groups["type"].Value,
            Guid.Parse(match.Groups["uid"].Value),
            Uri.UnescapeDataString(match.Groups["token"].Value));
    }

    [GeneratedRegex(@"definir-senha\?tipo=(?<type>\w+)&uid=(?<uid>[0-9a-f-]+)&token=(?<token>\S+)")]
    private static partial Regex LinkRegex();
}

public sealed record PasswordLink(string Type, Guid UserId, string Token);
