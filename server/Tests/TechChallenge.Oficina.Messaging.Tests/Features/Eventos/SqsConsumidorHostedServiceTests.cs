using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Controllers.Features.Eventos;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.Messaging.Features.Eventos;
using TechChallenge.Oficina.Messaging.Sqs;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using Xunit;

namespace TechChallenge.Oficina.Messaging.Tests.Features.Eventos;

public sealed class SqsConsumidorHostedServiceTests : IDisposable
{
    private const string UrlFila = "http://localhost:9324/000000000000/techchallenge-oficina-management.fifo";
    private const string ReceiptHandle = "receipt-handle-1";

    private readonly Mock<ISqsClient> _sqsClientMock = new();
    private readonly Mock<IEventosController> _eventosControllerMock = new();
    private readonly SqsSettings _sqsSettings = new() { FilaConsumo = "techchallenge-oficina-management.fifo" };
    private readonly ServiceProvider _serviceProvider;

    public SqsConsumidorHostedServiceTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _eventosControllerMock.Object);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose() => _serviceProvider.Dispose();

    private SqsConsumidorHostedService CriarConsumidor(ILogger<SqsConsumidorHostedService>? logger = null)
    {
        return new SqsConsumidorHostedService(
            _sqsClientMock.Object,
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _sqsSettings,
            logger ?? NullLogger<SqsConsumidorHostedService>.Instance);
    }

    private static MensagemSqs CriarMensagem(string tipo, string dadosJson, Guid? eventoId = null)
    {
        var corpo = $$"""{"id":"{{eventoId ?? Guid.NewGuid()}}","tipo":"{{tipo}}","versao":1,"ocorridoEm":"2026-11-14T10:00:00Z","origem":"os-service","dados":{{dadosJson}}}""";
        return new MensagemSqs("message-id-1", ReceiptHandle, corpo);
    }

    private static (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeLog(Mock<ILogger<SqsConsumidorHostedService>> loggerMock, EventId evento)
    {
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_DeveRepassarTipoEDadosEExcluirMensagem_QuandoProcessado()
    {
        var loggerMock = new Mock<ILogger<SqsConsumidorHostedService>>();
        var consumidor = CriarConsumidor(loggerMock.Object);
        var ordemServicoId = Guid.NewGuid();
        var eventoId = Guid.NewGuid();
        var dados = $$"""{"ordemServicoId":"{{ordemServicoId}}"}""";
        _eventosControllerMock
            .Setup(controller => controller.ProcessarAsync("OrdemServicoExcluida", dados, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var removida = await consumidor.ProcessarMensagemAsync(UrlFila, CriarMensagem("OrdemServicoExcluida", dados, eventoId), CancellationToken.None);

        Assert.True(removida);
        _eventosControllerMock.Verify(controller => controller.ProcessarAsync("OrdemServicoExcluida", dados, It.IsAny<CancellationToken>()), Times.Once);
        _sqsClientMock.Verify(client => client.DeleteMessageAsync(UrlFila, ReceiptHandle, It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.EventoConsumido);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal("OrdemServicoExcluida", propriedades["Tipo"]);
        Assert.Equal(eventoId.ToString(), propriedades["EventoId"]);
        Assert.Equal(ordemServicoId.ToString(), propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_DeveExcluirMensagemERegistrarIgnorado_QuandoIgnorado()
    {
        var loggerMock = new Mock<ILogger<SqsConsumidorHostedService>>();
        var consumidor = CriarConsumidor(loggerMock.Object);
        _eventosControllerMock
            .Setup(controller => controller.ProcessarAsync("EventoDesconhecido", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Ignorado);

        var removida = await consumidor.ProcessarMensagemAsync(UrlFila, CriarMensagem("EventoDesconhecido", "{}"), CancellationToken.None);

        Assert.True(removida);
        _sqsClientMock.Verify(client => client.DeleteMessageAsync(UrlFila, ReceiptHandle, It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.EventoIgnorado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal("EventoDesconhecido", propriedades["Tipo"]);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.EventoConsumido.Id);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_NaoDeveExcluirMensagemERegistrarFalha_QuandoProcessamentoFalhar()
    {
        var loggerMock = new Mock<ILogger<SqsConsumidorHostedService>>();
        var consumidor = CriarConsumidor(loggerMock.Object);
        var falha = new InvalidOperationException("Banco indisponivel.");
        _eventosControllerMock
            .Setup(controller => controller.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var removida = await consumidor.ProcessarMensagemAsync(UrlFila, CriarMensagem("OrdemServicoAberta", "{}"), CancellationToken.None);

        Assert.False(removida);
        _sqsClientMock.Verify(client => client.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var (nivel, propriedades, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecaoRegistrada);
        Assert.Equal("sqs", propriedades["Integracao"]);
        Assert.Equal("consumo", propriedades["Operacao"]);
        Assert.Equal("OrdemServicoAberta", propriedades["Tipo"]);
        Assert.Equal("message-id-1", propriedades["MessageId"]);
    }

    [Theory]
    [InlineData("isto nao e json")]
    [InlineData("""{"id":"1","tipo":"OrdemServicoAberta"}""")]
    [InlineData("""{"id":"1","dados":{}}""")]
    public async Task ProcessarMensagemAsync_NaoDeveExcluirMensagem_QuandoEnvelopeInvalido(string corpo)
    {
        var loggerMock = new Mock<ILogger<SqsConsumidorHostedService>>();
        var consumidor = CriarConsumidor(loggerMock.Object);

        var removida = await consumidor.ProcessarMensagemAsync(UrlFila, new MensagemSqs("message-id-1", ReceiptHandle, corpo), CancellationToken.None);

        Assert.False(removida);
        _eventosControllerMock.Verify(controller => controller.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _sqsClientMock.Verify(client => client.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        var (nivel, _, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
    }

    [Fact]
    public async Task ProcessarMensagemAsync_DevePropagarCancelamentoSemExcluir_QuandoAplicacaoEncerrando()
    {
        var consumidor = CriarConsumidor();
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _eventosControllerMock
            .Setup(controller => controller.ProcessarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await consumidor.ProcessarMensagemAsync(UrlFila, CriarMensagem("OrdemServicoAberta", "{}"), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        _sqsClientMock.Verify(client => client.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DeveConsumirMensagensDaFilaAteSerParado()
    {
        var consumidor = CriarConsumidor();
        var mensagem = CriarMensagem("OrdemServicoExcluida", $$"""{"ordemServicoId":"{{Guid.NewGuid()}}"}""");
        var mensagemExcluida = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recebimentos = 0;
        _sqsClientMock
            .Setup(client => client.GetQueueUrlAsync(_sqsSettings.FilaConsumo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UrlFila);
        _sqsClientMock
            .Setup(client => client.ReceiveMessagesAsync(UrlFila, 10, 20, It.IsAny<CancellationToken>()))
            .Returns(async (string _, int _, int _, CancellationToken cancellationToken) =>
            {
                if (Interlocked.Increment(ref recebimentos) == 1)
                {
                    return [mensagem];
                }

                // Long polling sem mensagens: aguarda ate o encerramento da aplicacao
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return [];
            });
        _sqsClientMock
            .Setup(client => client.DeleteMessageAsync(UrlFila, ReceiptHandle, It.IsAny<CancellationToken>()))
            .Callback(() => mensagemExcluida.TrySetResult())
            .Returns(Task.CompletedTask);
        _eventosControllerMock
            .Setup(controller => controller.ProcessarAsync("OrdemServicoExcluida", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        await consumidor.StartAsync(CancellationToken.None);
        await mensagemExcluida.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await consumidor.StopAsync(CancellationToken.None);

        _eventosControllerMock.Verify(controller => controller.ProcessarAsync("OrdemServicoExcluida", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(consumidor.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_DeveInterromperLoteAposFalha_ParaPreservarOrdemFifo()
    {
        var consumidor = CriarConsumidor();
        var ordemServicoId = Guid.NewGuid();
        var mensagemComFalha = new MensagemSqs("message-id-1", "receipt-falha", CriarMensagem("OrdemServicoAberta", $$"""{"ordemServicoId":"{{ordemServicoId}}"}""").Body);
        var mensagemSeguinte = new MensagemSqs("message-id-2", "receipt-seguinte", CriarMensagem("OrdemServicoStatusAlterado", $$"""{"ordemServicoId":"{{ordemServicoId}}"}""").Body);
        var loteConcluido = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recebimentos = 0;
        _sqsClientMock
            .Setup(client => client.GetQueueUrlAsync(_sqsSettings.FilaConsumo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UrlFila);
        _sqsClientMock
            .Setup(client => client.ReceiveMessagesAsync(UrlFila, 10, 20, It.IsAny<CancellationToken>()))
            .Returns(async (string _, int _, int _, CancellationToken cancellationToken) =>
            {
                if (Interlocked.Increment(ref recebimentos) == 1)
                {
                    return [mensagemComFalha, mensagemSeguinte];
                }

                loteConcluido.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return [];
            });
        _eventosControllerMock
            .Setup(controller => controller.ProcessarAsync("OrdemServicoAberta", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Banco indisponivel."));

        await consumidor.StartAsync(CancellationToken.None);
        await loteConcluido.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await consumidor.StopAsync(CancellationToken.None);

        _eventosControllerMock.Verify(controller => controller.ProcessarAsync("OrdemServicoStatusAlterado", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _sqsClientMock.Verify(client => client.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
