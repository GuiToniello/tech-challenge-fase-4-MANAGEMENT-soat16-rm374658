using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Messaging.Configuration;
using TechChallenge.Oficina.Messaging.Features.Eventos;
using TechChallenge.Oficina.Messaging.Sqs;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using Xunit;

namespace TechChallenge.Oficina.Messaging.Tests.Features.Eventos;

public sealed class EventosPublisherTests
{
    private const string UrlFila = "http://localhost:9324/000000000000/techchallenge-oficina-os-service.fifo";

    private readonly Mock<ISqsClient> _sqsClientMock = new();
    private readonly SqsSettings _sqsSettings = new() { FilaPublicacao = "techchallenge-oficina-os-service.fifo" };

    private EventosPublisher CriarPublisher(ILogger<EventosPublisher>? logger = null)
    {
        _sqsClientMock
            .Setup(client => client.GetQueueUrlAsync(_sqsSettings.FilaPublicacao, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UrlFila);

        return new EventosPublisher(_sqsClientMock.Object, _sqsSettings, logger ?? NullLogger<EventosPublisher>.Instance);
    }

    private (Func<string> Corpo, Func<string> Tipo, Func<string> Grupo, Func<string> Deduplicacao) CapturarEnvio()
    {
        string? corpo = null, tipo = null, grupo = null, deduplicacao = null;

        _sqsClientMock
            .Setup(client => client.SendMessageAsync(UrlFila, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((_, body, tipoMensagem, groupId, deduplicationId, _) =>
            {
                corpo = body;
                tipo = tipoMensagem;
                grupo = groupId;
                deduplicacao = deduplicationId;
            })
            .Returns(Task.CompletedTask);

        return (() => corpo!, () => tipo!, () => grupo!, () => deduplicacao!);
    }

    private static (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeLog(Mock<ILogger<EventosPublisher>> loggerMock, EventId evento)
    {
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    [Fact]
    public async Task PublicarAsync_DeveEnviarEnvelopeComTipoGrupoEDeduplicacao()
    {
        var publisher = CriarPublisher();
        var envio = CapturarEnvio();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrcamentoGerado(ordemServicoId, 150.50m), ordemServicoId);

        Assert.Equal("OrcamentoGerado", envio.Tipo());
        Assert.Equal(ordemServicoId.ToString("D"), envio.Grupo());

        using var documento = JsonDocument.Parse(envio.Corpo());
        var envelope = documento.RootElement;
        Assert.Equal(envio.Deduplicacao(), envelope.GetProperty("id").GetString());
        Assert.True(Guid.TryParse(envio.Deduplicacao(), out _));
        Assert.Equal("OrcamentoGerado", envelope.GetProperty("tipo").GetString());
        Assert.Equal(1, envelope.GetProperty("versao").GetInt32());
        Assert.Equal("management", envelope.GetProperty("origem").GetString());
        Assert.EndsWith("Z", envelope.GetProperty("ocorridoEm").GetString());
        Assert.Equal(DateTimeKind.Utc, envelope.GetProperty("ocorridoEm").GetDateTime().Kind);

        var dados = envelope.GetProperty("dados");
        Assert.Equal(ordemServicoId, dados.GetProperty("ordemServicoId").GetGuid());
        Assert.Equal(150.50m, dados.GetProperty("valorTotal").GetDecimal());
    }

    [Fact]
    public async Task PublicarAsync_DeveSerializarDadosEmCamelCase()
    {
        var publisher = CriarPublisher();
        var envio = CapturarEnvio();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Cliente nao encontrado."), ordemServicoId);

        using var documento = JsonDocument.Parse(envio.Corpo());
        var dados = documento.RootElement.GetProperty("dados");
        Assert.Equal("OrdemServicoRejeitada", envio.Tipo());
        Assert.Equal("Cliente nao encontrado.", dados.GetProperty("motivo").GetString());
        Assert.False(dados.TryGetProperty("Motivo", out _));
    }

    [Fact]
    public async Task PublicarAsync_DeveGerarIdDiferenteParaCadaEvento()
    {
        var publisher = CriarPublisher();
        var envio = CapturarEnvio();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrcamentoAprovado(ordemServicoId), ordemServicoId);
        var primeiraDeduplicacao = envio.Deduplicacao();
        await publisher.PublicarAsync(new OrcamentoAprovado(ordemServicoId), ordemServicoId);

        Assert.NotEqual(primeiraDeduplicacao, envio.Deduplicacao());
    }

    [Fact]
    public async Task PublicarAsync_DeveResolverUrlDaFilaUmaUnicaVez()
    {
        var publisher = CriarPublisher();
        CapturarEnvio();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrcamentoGerado(ordemServicoId, 10m), ordemServicoId);
        await publisher.PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId);

        _sqsClientMock.Verify(client => client.GetQueueUrlAsync(_sqsSettings.FilaPublicacao, It.IsAny<CancellationToken>()), Times.Once);
        _sqsClientMock.Verify(client => client.SendMessageAsync(UrlFila, It.IsAny<string>(), It.IsAny<string>(), ordemServicoId.ToString("D"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarEventoPublicado()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var publisher = CriarPublisher(loggerMock.Object);
        CapturarEnvio();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrcamentoAprovado(ordemServicoId), ordemServicoId);

        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.EventoPublicado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal("OrcamentoAprovado", propriedades["Tipo"]);
        Assert.Equal(ordemServicoId, propriedades["OrdemServicoId"]);
        Assert.Equal(_sqsSettings.FilaPublicacao, propriedades["Fila"]);
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoEnvioFalhar()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var publisher = CriarPublisher(loggerMock.Object);
        var ordemServicoId = Guid.NewGuid();
        var falha = new HttpRequestException("SQS indisponivel.");
        _sqsClientMock
            .Setup(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await publisher.PublicarAsync(new OrcamentoGerado(ordemServicoId, 10m), ordemServicoId);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Same(falha, exception);
        var (nivel, propriedades, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecaoRegistrada);
        Assert.Equal("sqs", propriedades["Integracao"]);
        Assert.Equal("publicacao", propriedades["Operacao"]);
        Assert.Equal("OrcamentoGerado", propriedades["Tipo"]);
        Assert.Equal(ordemServicoId, propriedades["OrdemServicoId"]);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.EventoPublicado.Id);
    }

    [Fact]
    public async Task PublicarAsync_DeveTentarResolverUrlNovamente_QuandoPrimeiraTentativaFalhar()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var publisher = new EventosPublisher(_sqsClientMock.Object, _sqsSettings, loggerMock.Object);
        CapturarEnvio();
        var ordemServicoId = Guid.NewGuid();
        _sqsClientMock
            .SetupSequence(client => client.GetQueueUrlAsync(_sqsSettings.FilaPublicacao, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fila inexistente."))
            .ReturnsAsync(UrlFila);

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublicarAsync(new OrcamentoAprovado(ordemServicoId), ordemServicoId));
        await publisher.PublicarAsync(new OrcamentoAprovado(ordemServicoId), ordemServicoId);

        _sqsClientMock.Verify(client => client.GetQueueUrlAsync(_sqsSettings.FilaPublicacao, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _sqsClientMock.Verify(client => client.SendMessageAsync(UrlFila, It.IsAny<string>(), "OrcamentoAprovado", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublicarAsync_DeveRelancarSemRegistrarFalha_QuandoCancelado()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var publisher = CriarPublisher(loggerMock.Object);
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _sqsClientMock
            .Setup(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await publisher.PublicarAsync(new OrcamentoAprovado(Guid.NewGuid()), Guid.NewGuid(), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
    }
}
