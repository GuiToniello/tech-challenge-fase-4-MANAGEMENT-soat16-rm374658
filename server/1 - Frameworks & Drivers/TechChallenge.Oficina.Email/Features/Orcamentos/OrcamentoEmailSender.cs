using System.Globalization;
using System.Text;
using Resend;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Email.Configuration;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

namespace TechChallenge.Oficina.Email.Features.Orcamentos;

public sealed class OrcamentoEmailSender : IOrcamentoEmailSender
{
    private readonly IResendClient _resendClient;
    private readonly ResendSettings _resendSettings;

    public OrcamentoEmailSender(IResendClient resendClient, ResendSettings resendSettings)
    {
        _resendClient = resendClient;
        _resendSettings = resendSettings;
    }

    public async Task EnviarOrcamentoAsync(Orcamento orcamento, string emailDestino, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orcamento);

        if (string.IsNullOrWhiteSpace(_resendSettings.ApiKey) || string.IsNullOrWhiteSpace(_resendSettings.FromEmail))
        {
            throw new DomainException("Servico de email nao esta configurado. Verifique as configuracoes do Resend.");
        }

        var assunto = CriarAssunto(orcamento);
        var html = CriarHtml(orcamento);

        await _resendClient.SendEmailAsync(new EmailMessage
        {
            From = _resendSettings.FromEmail,
            To = emailDestino,
            Subject = assunto,
            HtmlBody = html
        }, cancellationToken);
    }

    private static string CriarAssunto(Orcamento orcamento)
    {
        var data = orcamento.DataGeracao.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var prefixo = orcamento.OrdemServicoId.ToString()[..8].ToUpperInvariant();
        return $"Orçamento {prefixo} ({orcamento.OrdemServicoId}) - data: {data}";
    }

    private static string CriarHtml(Orcamento orcamento)
    {
        var cultura = new CultureInfo("pt-BR");
        var builder = new StringBuilder();

        builder.Append("<h1>Orçamento da Ordem de Serviço</h1>");
        builder.Append($"<p><strong>Ordem de Serviço:</strong> {orcamento.OrdemServicoId}</p>");
        builder.Append($"<p><strong>Status do orçamento:</strong> {orcamento.Status.ObterDescricao()}</p>");
        builder.Append($"<p><strong>Data de geração:</strong> {orcamento.DataGeracao.ToLocalTime():dd/MM/yyyy HH:mm}</p>");
        builder.Append($"<p><strong>Valor total:</strong> {orcamento.ValorTotal.ToString("C", cultura)}</p>");

        builder.Append("<h2>Serviços</h2>");
        builder.Append("<table border='1' cellpadding='8' cellspacing='0' style='border-collapse: collapse;'>");
        builder.Append("<thead><tr><th>Serviço</th><th>Valor</th></tr></thead>");
        builder.Append("<tbody>");

        foreach (var servico in orcamento.Servicos)
        {
            builder.Append("<tr>");
            builder.Append($"<td>{servico.NomeServico} ({servico.ServicoId})</td>");
            builder.Append($"<td>{servico.ValorTotal.ToString("C", cultura)}</td>");
            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");

        return builder.ToString();
    }
}
