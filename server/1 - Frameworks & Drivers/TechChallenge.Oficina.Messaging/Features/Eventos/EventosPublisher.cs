using System.Text.Json;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.Messaging.Sqs;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;

namespace TechChallenge.Oficina.Messaging.Features.Eventos;

// Publica os eventos do Management na fila FIFO do OS Service: MessageGroupId = OS (ordem garantida por OS) e MessageDeduplicationId = id do envelope.
public sealed class EventosPublisher : IEventosPublisher
{
    public const string Origem = "management";
    public const int Versao = 1;

    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    private readonly ISqsClient _sqsClient;
    private readonly SqsSettings _sqsSettings;
    private readonly ILogger<EventosPublisher> _logger;
    private string? _urlFilaPublicacao;

    public EventosPublisher(ISqsClient sqsClient, SqsSettings sqsSettings, ILogger<EventosPublisher> logger)
    {
        _sqsClient = sqsClient;
        _sqsSettings = sqsSettings;
        _logger = logger;
    }

    public async Task PublicarAsync<TEvento>(TEvento evento, Guid ordemServicoId, CancellationToken cancellationToken = default) where TEvento : class
    {
        var envelope = new EnvelopeEvento<TEvento>(Guid.NewGuid(), typeof(TEvento).Name, Versao, DateTime.UtcNow, Origem, evento);
        var corpo = JsonSerializer.Serialize(envelope, OpcoesJson);

        try
        {
            var urlFila = await ObterUrlFilaPublicacaoAsync(cancellationToken);
            await _sqsClient.SendMessageAsync(urlFila, corpo, envelope.Tipo, ordemServicoId.ToString("D"), envelope.Id.ToString("D"), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                ex,
                "Falha na integracao {Integracao} durante a operacao {Operacao} do evento {Tipo} da ordem de servico {OrdemServicoId}.",
                OrdemServicoLogEventos.Integracoes.Sqs,
                OrdemServicoLogEventos.Operacoes.Publicacao,
                envelope.Tipo,
                ordemServicoId);
            throw;
        }

        _logger.LogInformation(
            OrdemServicoLogEventos.EventoPublicado,
            "Evento {Tipo} {EventoId} da ordem de servico {OrdemServicoId} publicado na fila {Fila}.",
            envelope.Tipo,
            envelope.Id,
            ordemServicoId,
            _sqsSettings.FilaPublicacao);
    }

    private async Task<string> ObterUrlFilaPublicacaoAsync(CancellationToken cancellationToken)
    {
        // A URL da fila nao muda durante a execucao: e resolvida uma vez e reaproveitada (o publicador e singleton)
        _urlFilaPublicacao ??= await _sqsClient.GetQueueUrlAsync(_sqsSettings.FilaPublicacao, cancellationToken);
        return _urlFilaPublicacao;
    }
}
