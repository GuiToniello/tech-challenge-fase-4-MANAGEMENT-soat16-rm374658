using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.Messaging.Sqs;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;

namespace TechChallenge.Oficina.Messaging.Features.Eventos;

// Publica os eventos do Management nas filas FIFO de destino (OS Service e Billing): o envelope e montado uma vez e o mesmo (mesmo id) vai para cada fila, na ordem configurada.
// MessageGroupId = OS (ordem garantida por OS) e MessageDeduplicationId = id do envelope.
public sealed class EventosPublisher : IEventosPublisher
{
    public const string Origem = "management";
    public const int Versao = 1;

    private static readonly JsonSerializerOptions OpcoesJson = new(JsonSerializerDefaults.Web);

    private readonly ISqsClient _sqsClient;
    private readonly SqsSettings _sqsSettings;
    private readonly ILogger<EventosPublisher> _logger;
    private readonly ConcurrentDictionary<string, string> _urlsFilas = new();

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

        // Sem transacao entre as filas: se o envio para uma fila falhar, as anteriores ja receberam o evento (entrega parcial aceita) e a excecao e propagada
        foreach (var fila in _sqsSettings.FilasPublicacao)
        {
            await PublicarNaFilaAsync(fila, corpo, envelope.Tipo, envelope.Id, ordemServicoId, cancellationToken);
        }
    }

    private async Task PublicarNaFilaAsync(string fila, string corpo, string tipo, Guid eventoId, Guid ordemServicoId, CancellationToken cancellationToken)
    {
        try
        {
            var urlFila = await ObterUrlFilaAsync(fila, cancellationToken);
            await _sqsClient.SendMessageAsync(urlFila, corpo, tipo, ordemServicoId.ToString("D"), eventoId.ToString("D"), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                ex,
                "Falha na integracao {Integracao} durante a operacao {Operacao} do evento {Tipo} da ordem de servico {OrdemServicoId} na fila {Fila}.",
                OrdemServicoLogEventos.Integracoes.Sqs,
                OrdemServicoLogEventos.Operacoes.Publicacao,
                tipo,
                ordemServicoId,
                fila);
            throw;
        }

        _logger.LogInformation(
            OrdemServicoLogEventos.EventoPublicado,
            "Evento {Tipo} {EventoId} da ordem de servico {OrdemServicoId} publicado na fila {Fila}.",
            tipo,
            eventoId,
            ordemServicoId,
            fila);
    }

    private async Task<string> ObterUrlFilaAsync(string fila, CancellationToken cancellationToken)
    {
        // A URL de cada fila nao muda durante a execucao: e resolvida uma vez e reaproveitada (o publicador e singleton)
        if (!_urlsFilas.TryGetValue(fila, out var urlFila))
        {
            urlFila = await _sqsClient.GetQueueUrlAsync(fila, cancellationToken);
            _urlsFilas[fila] = urlFila;
        }

        return urlFila;
    }
}
