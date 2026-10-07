using System.Net;
using SmartStock.Application.Abstractions;

namespace SmartStock.Infrastructure.Email;

internal static class EmailTemplates
{
    public static EmailMessage Invite(string to, string fullName, string link, int validHours) =>
        Build(
            to,
            subject: "SmartStock AI: ative seu acesso",
            greeting: $"Olá, {fullName}!",
            paragraph: "Um acesso ao SmartStock AI foi criado para você. Clique no botão abaixo para criar sua senha.",
            buttonText: "Criar minha senha",
            link: link,
            note: $"Este link é válido por {validHours} horas e só pode ser usado uma vez.");

    public static EmailMessage PasswordReset(string to, string fullName, string link, int validHours) =>
        Build(
            to,
            subject: "SmartStock AI: redefinição de senha",
            greeting: $"Olá, {fullName}!",
            paragraph: "Recebemos um pedido para redefinir sua senha. Clique no botão abaixo para criar uma nova.",
            buttonText: "Redefinir senha",
            link: link,
            note: $"Este link é válido por {validHours} hora(s). Se você não pediu a redefinição, ignore este e-mail: sua senha continua a mesma.");

    public static EmailMessage AlertSummary(string to, string fullName, string summary, string link) =>
        Build(
            to,
            subject: "SmartStock AI: alertas da análise do estoque",
            greeting: $"Olá, {fullName}!",
            paragraph: summary,
            buttonText: "Ver os alertas",
            link: link,
            note: "Os alertas só avisam: nenhuma ação é executada pelo sistema. Para parar de receber, o Administrador desliga o envio nos parâmetros da análise.");

    private static EmailMessage Build(
        string to, string subject, string greeting, string paragraph, string buttonText, string link, string note)
    {
        Func<string, string> h = WebUtility.HtmlEncode;

        var html = $"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <body style="margin:0;padding:24px;background:#F4F6FA;font-family:Arial,Helvetica,sans-serif;color:#1F2A44;">
              <table role="presentation" width="100%" style="max-width:520px;margin:0 auto;background:#FFFFFF;border-radius:12px;padding:32px;">
                <tr><td>
                  <p style="margin:0 0 24px;font-size:20px;font-weight:bold;color:#1273BA;">SmartStock AI</p>
                  <p style="margin:0 0 16px;font-size:16px;">{h(greeting)}</p>
                  <p style="margin:0 0 24px;font-size:15px;line-height:1.5;">{h(paragraph)}</p>
                  <p style="margin:0 0 24px;">
                    <a href="{h(link)}" style="display:inline-block;background:#1273BA;color:#FFFFFF;text-decoration:none;padding:12px 24px;border-radius:8px;font-weight:bold;">{h(buttonText)}</a>
                  </p>
                  <p style="margin:0 0 8px;font-size:13px;color:#5B6478;">{h(note)}</p>
                  <p style="margin:0;font-size:12px;color:#8A93A6;word-break:break-all;">Se o botão não funcionar, copie este endereço no navegador:<br>{h(link)}</p>
                </td></tr>
              </table>
              <p style="text-align:center;font-size:12px;color:#8A93A6;margin-top:16px;">Dorémi Brinquedos · Mensagem automática, não responda.</p>
            </body>
            </html>
            """;

        var text = $"""
            {greeting}

            {paragraph}

            {buttonText}: {link}

            {note}

            Dorémi Brinquedos · Mensagem automática, não responda.
            """;

        return new EmailMessage(to, subject, html, text);
    }
}
