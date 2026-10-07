using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartStock.Application.Abstractions;

namespace SmartStock.Infrastructure.Email;

/// <summary>
/// Somente desenvolvimento: grava o e-mail como arquivo .eml em vez de enviar,
/// e escreve o texto no log para facilitar os testes (inclui o link de acesso).
/// </summary>
internal sealed class FileEmailSender(IOptions<EmailOptions> options, ILogger<FileEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetFullPath(_options.PickupDirectory);
        Directory.CreateDirectory(directory);

        var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{SanitizeFileName(message.To)}.eml";
        var path = Path.Combine(directory, fileName);

        var mime = MimeMessageFactory.Create(message, _options);
        await using (var stream = File.Create(path))
            await mime.WriteToAsync(stream, cancellationToken);

        logger.LogWarning(
            "[DEV] E-mail NÃO enviado (modo arquivo). Para: {To} | Assunto: {Subject} | Arquivo: {Path}{NewLine}{Body}",
            message.To, message.Subject, path, Environment.NewLine, message.TextBody);
    }

    private static string SanitizeFileName(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
