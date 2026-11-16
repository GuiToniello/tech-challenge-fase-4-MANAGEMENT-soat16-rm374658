using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.Controllers.Features.Eventos;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.Messaging.Sqs;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;

namespace TechChallenge.Oficina.Messaging.Features.Eventos;

// Consome a fila FIFO do Management (eventos do OS Service) com long polling. A mensagem so e removida quando processada ou ignorada;
// em caso de falha ela volta para a fila apos o visibility timeout e, depois de 5 recebimentos (maxReceiveCount), segue para a DLQ.
public sealed class SqsConsumidorHostedService : BackgroundService
{
    private const int MaximoMensagens = 10;
    private const int EsperaSegundos = 20;
    private const string TipoDesconhecido = "desconhecido";
    private static readonly TimeSpan IntervaloNovaTentativa = TimeSpan.FromSeconds(10);

    private readonly ISqsClient _sqsClient;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly SqsSettings _sqsSettings;
    private readonly ILogger<SqsConsumidorHostedService> _logger;

    public SqsConsumidorHostedService(
        ISqsClient sqsClient,
        IServiceScopeFactory serviceScopeFactory,
        SqsSettings sqsSettings,
        ILogger<SqsConsumidorHostedService> logger)
    {
        _sqsClient = sqsClient;
        _serviceScopeFactory = serviceScopeFactory;
        _sqsSettings = sqsSettings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var urlFila = await ObterUrlFilaConsumoAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await ReceberEProcessarAsync(urlFila, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento da aplicacao: mensagens nao removidas voltam para a fila apos o visibility timeout
        }
    }

    // Processa uma mensagem do envelope e indica se ela foi removida da fila; publico para permitir o teste unitario sem o laco de long polling.
    public async Task<bool> ProcessarMensagemAsync(string urlFila, MensagemSqs mensagem, CancellationToken cancellationToken)
    {
        var tipo = TipoDesconhecido;
        string? eventoId = null;
        string? ordemServicoId = null;

        try
        {
            string dadosJson;

            using (var documento = JsonDocument.Parse(mensagem.Body))
            {
                var envelope = documento.RootElement;
                tipo = envelope.GetProperty("tipo").GetString() ?? TipoDesconhecido;
                eventoId = envelope.TryGetProperty("id", out var id) ? id.ToString() : null;

                var dados = envelope.GetProperty("dados");
                ordemServicoId = dados.ValueKind == JsonValueKind.Object && dados.TryGetProperty("ordemServicoId", out var ordemServico)
                    ? ordemServico.ToString()
                    : null;
                dadosJson = dados.GetRawText();
            }

            ResultadoProcessamentoEvento resultado;

            // Um escopo de DI por mensagem (DbContext e casos de uso scoped, como em uma requisicao HTTP)
            await using (var scope = _serviceScopeFactory.CreateAsyncScope())
            {
                var eventosController = scope.ServiceProvider.GetRequiredService<IEventosController>();
                resultado = await eventosController.ProcessarAsync(tipo, dadosJson, cancellationToken);
            }

            await _sqsClient.DeleteMessageAsync(urlFila, mensagem.ReceiptHandle, cancellationToken);

            _logger.LogInformation(
                resultado == ResultadoProcessamentoEvento.Processado ? OrdemServicoLogEventos.EventoConsumido : OrdemServicoLogEventos.EventoIgnorado,
                "Evento {Tipo} {EventoId} da ordem de servico {OrdemServicoId} {Resultado} (mensagem {MessageId} da fila {Fila}).",
                tipo,
                eventoId,
                ordemServicoId,
                resultado == ResultadoProcessamentoEvento.Processado ? "consumido" : "ignorado",
                mensagem.MessageId,
                _sqsSettings.FilaConsumo);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // A mensagem nao e removida: volta para a fila para nova tentativa e, esgotadas as tentativas, vai para a DLQ
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                ex,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da mensagem {MessageId} (evento {Tipo} {EventoId} da ordem de servico {OrdemServicoId}).",
                OrdemServicoLogEventos.Integracoes.Sqs,
                OrdemServicoLogEventos.Operacoes.Consumo,
                mensagem.MessageId,
                tipo,
                eventoId,
                ordemServicoId);

            return false;
        }
    }

    private async Task<string> ObterUrlFilaConsumoAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                var urlFila = await _sqsClient.GetQueueUrlAsync(_sqsSettings.FilaConsumo, stoppingToken);
                _logger.LogInformation("Consumidor SQS iniciado na fila {Fila}.", _sqsSettings.FilaConsumo);
                return urlFila;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                RegistrarFalhaConsumo(ex, "obter a URL da fila");
                await Task.Delay(IntervaloNovaTentativa, stoppingToken);
            }
        }
    }

    private async Task ReceberEProcessarAsync(string urlFila, CancellationToken stoppingToken)
    {
        IReadOnlyCollection<MensagemSqs> mensagens;

        try
        {
            mensagens = await _sqsClient.ReceiveMessagesAsync(urlFila, MaximoMensagens, EsperaSegundos, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            RegistrarFalhaConsumo(ex, "receber mensagens da fila");
            await Task.Delay(IntervaloNovaTentativa, stoppingToken);
            return;
        }

        foreach (var mensagem in mensagens)
        {
            // FIFO: o lote pode trazer varias mensagens da mesma OS em ordem; apos uma falha as demais nao sao processadas
            // e voltam para a fila apos o visibility timeout, preservando a ordem dos eventos de cada OS.
            if (!await ProcessarMensagemAsync(urlFila, mensagem, stoppingToken))
            {
                break;
            }
        }
    }

    private void RegistrarFalhaConsumo(Exception exception, string acao)
    {
        _logger.LogError(
            OrdemServicoLogEventos.IntegracaoFalha,
            exception,
            "Falha na integracao {Integracao} durante a operacao {Operacao} ao {Acao} {Fila}; nova tentativa em {IntervaloSegundos} segundos.",
            OrdemServicoLogEventos.Integracoes.Sqs,
            OrdemServicoLogEventos.Operacoes.Consumo,
            acao,
            _sqsSettings.FilaConsumo,
            IntervaloNovaTentativa.TotalSeconds);
    }
}
