using System.Text;
using Resend;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Email.Configuration;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

namespace TechChallenge.Oficina.Email.Features.OrdensServico;

/// <summary>
/// Implementação do gateway de envio de email para notificações de mudança de status de ordem de serviço.
/// Segue o padrão estabelecido em ADR-011, reutilizando Resend e modo degradado quando sem configuração.
/// </summary>
public sealed class OrdemServicoStatusEmailSender : IOrdemServicoStatusEmailSender
{
    private readonly IResendClient _resendClient;
    private readonly ResendSettings _resendSettings;

    public OrdemServicoStatusEmailSender(IResendClient resendClient, ResendSettings resendSettings)
    {
        _resendClient = resendClient;
        _resendSettings = resendSettings;
    }

    public async Task EnviarStatusAlteradoAsync(
        NotificacaoStatusOrdemServico notificacao,
        string emailDestino,
        CancellationToken cancellationToken = default)
    {
        // Modo degradado: se não houver configuração ou se o envio de emails de status estiver desabilitado, apenas retorna sem erro
        if (string.IsNullOrWhiteSpace(_resendSettings.ApiKey) ||
            string.IsNullOrWhiteSpace(_resendSettings.FromEmail) ||
            !_resendSettings.SendEmailOnStatusChange)
        {
            return;
        }

        var assunto = CriarAssunto(notificacao);
        var html = CriarHtml(notificacao);

        await _resendClient.SendEmailAsync(new EmailMessage
        {
            From = _resendSettings.FromEmail,
            To = emailDestino,
            Subject = assunto,
            HtmlBody = html
        }, cancellationToken);
    }

    private static string CriarAssunto(NotificacaoStatusOrdemServico notificacao)
    {
        var prefixo = notificacao.OrdemServicoId.ToString()[..8].ToUpperInvariant();
        var descricaoStatus = notificacao.StatusNovo.ObterDescricao();
        return $"Ordem de Serviço {prefixo} - Status: {descricaoStatus}";
    }

    private static string CriarHtml(NotificacaoStatusOrdemServico notificacao)
    {
        var builder = new StringBuilder();
        var descricaoStatus = notificacao.StatusNovo.ObterDescricao();

        builder.Append("<h1>Atualização de Status - Ordem de Serviço</h1>");
        builder.Append($"<p><strong>Ordem de Serviço:</strong> {notificacao.OrdemServicoId}</p>");
        builder.Append($"<p><strong>Cliente:</strong> {notificacao.ClienteId}</p>");
        builder.Append($"<p><strong>Veículo:</strong> {notificacao.VeiculoId}</p>");
        builder.Append($"<p><strong>Novo Status:</strong> <span style='color: #0066cc; font-weight: bold;'>{descricaoStatus}</span></p>");

        AppendDetalhesDoStatus(builder, notificacao.StatusNovo);

        builder.Append("<h2>Serviços da Ordem</h2>");
        builder.Append("<table border='1' cellpadding='8' cellspacing='0' style='border-collapse: collapse;'>");
        builder.Append("<thead><tr><th>Serviço</th></tr></thead>");
        builder.Append("<tbody>");

        foreach (var servico in notificacao.Servicos)
        {
            builder.Append("<tr>");
            builder.Append($"<td>{servico}</td>");
            builder.Append("</tr>");
        }

        builder.Append("</tbody></table>");
        builder.Append("<p><em>Se você tiver dúvidas sobre sua ordem de serviço, entre em contato conosco.</em></p>");

        return builder.ToString();
    }

    private static void AppendDetalhesDoStatus(StringBuilder builder, StatusOrdemServico status)
    {
        builder.Append("<h2>Detalhes da Transição</h2>");

        switch (status)
        {
            case StatusOrdemServico.EmDiagnostico:
                builder.Append("<p>Sua ordem de serviço entrou em fase de <strong>diagnóstico</strong>.</p>");
                builder.Append("<p>Nossos técnicos estão analisando o veículo para identificar os problemas e gerar um orçamento detalhado.</p>");
                break;

            case StatusOrdemServico.AguardandoAprovacao:
                builder.Append("<p>Sua ordem de serviço está <strong>aguardando aprovação</strong> do orçamento.</p>");
                builder.Append("<p>Verifique o email anterior com o orçamento detalhado e nos informe sua decisão.</p>");
                break;

            case StatusOrdemServico.EmExecucao:
                builder.Append("<p>Sua ordem de serviço está em <strong>execução</strong>.</p>");
                builder.Append("<p>Os serviços já iniciaram e nossos profissionais estão trabalhando em seu veículo.</p>");
                break;

            case StatusOrdemServico.Finalizada:
                builder.Append("<p>Sua ordem de serviço foi <strong>finalizada</strong>.</p>");
                builder.Append("<p>Todos os serviços foram concluídos conforme o orçamento aprovado.</p>");
                builder.Append("<p>Seu veículo está pronto para ser retirado.</p>");
                break;

            case StatusOrdemServico.Entregue:
                builder.Append("<p>Sua ordem de serviço foi <strong>entregue</strong>.</p>");
                builder.Append("<p>Obrigado por confiar em nossos serviços! Qualquer dúvida, estamos disponíveis.</p>");
                break;

            case StatusOrdemServico.Encerrada:
                builder.Append("<p>Sua ordem de serviço foi <strong>encerrada</strong>.</p>");
                builder.Append("<p>A OS foi finalizada e registrada em nossos arquivos.</p>");
                break;

            case StatusOrdemServico.Cancelada:
                builder.Append("<p>Sua ordem de serviço foi <strong>cancelada</strong>.</p>");
                builder.Append("<p>Em caso de dúvidas, entre em contato conosco.</p>");
                break;

            case StatusOrdemServico.Recebida:
            default:
                builder.Append("<p>Sua ordem de serviço foi <strong>recebida</strong>.</p>");
                builder.Append("<p>Aguardaremos o diagnóstico inicial.</p>");
                break;
        }
    }
}
