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
    private const string FilaOsService = "techchallenge-oficina-os-service.fifo";
    private const string FilaBilling = "techchallenge-oficina-billing.fifo";
    private const string UrlFilaOsService = "http://localhost:9324/000000000000/techchallenge-oficina-os-service.fifo";
    private const string UrlFilaBilling = "http://localhost:9324/000000000000/techchallenge-oficina-billing.fifo";

    private readonly Mock<ISqsClient> _sqsClientMock = new();
    private readonly SqsSettings _sqsSettings = new() { FilasPublicacao = [FilaOsService, FilaBilling] };

    private sealed record EnvioCapturado(string UrlFila, string Corpo, string Tipo, string Grupo, string Deduplicacao);

    private EventosPublisher CriarPublisher(ILogger<EventosPublisher>? logger = null)
    {
        _sqsClientMock.Setup(client => client.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>())).ReturnsAsync(UrlFilaOsService);
        _sqsClientMock.Setup(client => client.GetQueueUrlAsync(FilaBilling, It.IsAny<CancellationToken>())).ReturnsAsync(UrlFilaBilling);

        return new EventosPublisher(_sqsClientMock.Object, _sqsSettings, logger ?? NullLogger<EventosPublisher>.Instance);
    }

    private List<EnvioCapturado> CapturarEnvios()
    {
        var envios = new List<EnvioCapturado>();

        _sqsClientMock
            .Setup(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, string, CancellationToken>((url, body, tipo, groupId, deduplicationId, _) =>
                envios.Add(new EnvioCapturado(url, body, tipo, groupId, deduplicationId)))
            .Returns(Task.CompletedTask);

        return envios;
    }

    private static List<(LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao)> ObterRegistrosDeLog(Mock<ILogger<EventosPublisher>> loggerMock, EventId evento)
    {
        return loggerMock.Invocations
            .Where(chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id)
            .Select(chamada => (
                (LogLevel)chamada.Arguments[0],
                (IReadOnlyDictionary<string, object?>)((IEnumerable<KeyValuePair<string, object?>>)chamada.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value),
                (Exception?)chamada.Arguments[3]))
            .ToList();
    }

    [Fact]
    public async Task PublicarAsync_DeveEnviarEnvelopeComTipoGrupoEDeduplicacao()
    {
        var publisher = CriarPublisher();
        var envios = CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();
        var servicoId = Guid.NewGuid();
        var evento = new OrdemServicoValidada(ordemServicoId, clienteId, veiculoId, "cliente@teste.com", true, [new ServicoValidado(servicoId, "Troca de oleo", 150.50m)], 150.50m);

        await publisher.PublicarAsync(evento, ordemServicoId);

        var envio = envios[0];
        Assert.Equal("OrdemServicoValidada", envio.Tipo);
        Assert.Equal(ordemServicoId.ToString("D"), envio.Grupo);

        using var documento = JsonDocument.Parse(envio.Corpo);
        var envelope = documento.RootElement;
        Assert.Equal(envio.Deduplicacao, envelope.GetProperty("id").GetString());
        Assert.True(Guid.TryParse(envio.Deduplicacao, out _));
        Assert.Equal("OrdemServicoValidada", envelope.GetProperty("tipo").GetString());
        Assert.Equal(1, envelope.GetProperty("versao").GetInt32());
        Assert.Equal("management", envelope.GetProperty("origem").GetString());
        Assert.EndsWith("Z", envelope.GetProperty("ocorridoEm").GetString());
        Assert.Equal(DateTimeKind.Utc, envelope.GetProperty("ocorridoEm").GetDateTime().Kind);

        var dados = envelope.GetProperty("dados");
        Assert.Equal(ordemServicoId, dados.GetProperty("ordemServicoId").GetGuid());
        Assert.Equal(clienteId, dados.GetProperty("clienteId").GetGuid());
        Assert.Equal(veiculoId, dados.GetProperty("veiculoId").GetGuid());
        Assert.Equal("cliente@teste.com", dados.GetProperty("clienteEmail").GetString());
        Assert.True(dados.GetProperty("estoqueSuficiente").GetBoolean());
        Assert.Equal(150.50m, dados.GetProperty("valorTotal").GetDecimal());

        var servico = Assert.Single(dados.GetProperty("servicos").EnumerateArray());
        Assert.Equal(servicoId, servico.GetProperty("servicoId").GetGuid());
        Assert.Equal("Troca de oleo", servico.GetProperty("nome").GetString());
        Assert.Equal(150.50m, servico.GetProperty("valor").GetDecimal());
    }

    [Fact]
    public async Task PublicarAsync_DeveEnviarOMesmoEnvelopeParaCadaFilaNaOrdemConfigurada()
    {
        var publisher = CriarPublisher();
        var envios = CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Cliente nao encontrado."), ordemServicoId);

        Assert.Equal([UrlFilaOsService, UrlFilaBilling], envios.Select(envio => envio.UrlFila));
        Assert.Equal(envios[0].Corpo, envios[1].Corpo);
        Assert.Equal(envios[0].Deduplicacao, envios[1].Deduplicacao);
        Assert.All(envios, envio =>
        {
            Assert.Equal("OrdemServicoRejeitada", envio.Tipo);
            Assert.Equal(ordemServicoId.ToString("D"), envio.Grupo);
        });
    }

    [Fact]
    public async Task PublicarAsync_DeveSerializarDadosEmCamelCase()
    {
        var publisher = CriarPublisher();
        var envios = CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Cliente nao encontrado."), ordemServicoId);

        using var documento = JsonDocument.Parse(envios[0].Corpo);
        var dados = documento.RootElement.GetProperty("dados");
        Assert.Equal("Cliente nao encontrado.", dados.GetProperty("motivo").GetString());
        Assert.False(dados.TryGetProperty("Motivo", out _));
    }

    [Fact]
    public async Task PublicarAsync_DeveGerarIdDiferenteParaCadaEvento()
    {
        var publisher = CriarPublisher();
        var envios = CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo 1"), ordemServicoId);
        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo 2"), ordemServicoId);

        Assert.Equal(4, envios.Count);
        Assert.Equal(2, envios.Select(envio => envio.Deduplicacao).Distinct().Count());
    }

    [Fact]
    public async Task PublicarAsync_DeveResolverUrlDeCadaFilaUmaUnicaVez()
    {
        var publisher = CriarPublisher();
        CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo 1"), ordemServicoId);
        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo 2"), ordemServicoId);

        _sqsClientMock.Verify(client => client.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>()), Times.Once);
        _sqsClientMock.Verify(client => client.GetQueueUrlAsync(FilaBilling, It.IsAny<CancellationToken>()), Times.Once);
        _sqsClientMock.Verify(client => client.SendMessageAsync(UrlFilaOsService, It.IsAny<string>(), It.IsAny<string>(), ordemServicoId.ToString("D"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _sqsClientMock.Verify(client => client.SendMessageAsync(UrlFilaBilling, It.IsAny<string>(), It.IsAny<string>(), ordemServicoId.ToString("D"), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarEventoPublicadoParaCadaFila()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var publisher = CriarPublisher(loggerMock.Object);
        var envios = CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();

        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo"), ordemServicoId);

        var registros = ObterRegistrosDeLog(loggerMock, OrdemServicoLogEventos.EventoPublicado);
        Assert.Equal([FilaOsService, FilaBilling], registros.Select(registro => registro.Propriedades["Fila"]));
        Assert.All(registros, registro =>
        {
            Assert.Equal(LogLevel.Information, registro.Nivel);
            Assert.Equal("OrdemServicoRejeitada", registro.Propriedades["Tipo"]);
            Assert.Equal(ordemServicoId, registro.Propriedades["OrdemServicoId"]);
            Assert.Equal(Guid.Parse(envios[0].Deduplicacao), registro.Propriedades["EventoId"]);
        });
    }

    [Fact]
    public async Task PublicarAsync_DeveRegistrarFalhaDeIntegracaoERelancarSemEnviarAsDemais_QuandoEnvioFalhar()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var publisher = CriarPublisher(loggerMock.Object);
        var ordemServicoId = Guid.NewGuid();
        var falha = new HttpRequestException("SQS indisponivel.");
        _sqsClientMock
            .Setup(client => client.SendMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo"), ordemServicoId);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Same(falha, exception);
        var (nivel, propriedades, excecaoRegistrada) = Assert.Single(ObterRegistrosDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha));
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecaoRegistrada);
        Assert.Equal("sqs", propriedades["Integracao"]);
        Assert.Equal("publicacao", propriedades["Operacao"]);
        Assert.Equal("OrdemServicoRejeitada", propriedades["Tipo"]);
        Assert.Equal(ordemServicoId, propriedades["OrdemServicoId"]);
        Assert.Equal(FilaOsService, propriedades["Fila"]);
        Assert.Empty(ObterRegistrosDeLog(loggerMock, OrdemServicoLogEventos.EventoPublicado));
        _sqsClientMock.Verify(client => client.GetQueueUrlAsync(FilaBilling, It.IsAny<CancellationToken>()), Times.Never);
        _sqsClientMock.Verify(client => client.SendMessageAsync(UrlFilaBilling, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PublicarAsync_DeveRelancarComEntregaParcial_QuandoSegundaFilaFalhar()
    {
        var loggerMock = new Mock<ILogger<EventosPublisher>>();
        var publisher = CriarPublisher(loggerMock.Object);
        var envios = CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();
        var falha = new HttpRequestException("SQS indisponivel.");
        _sqsClientMock
            .Setup(client => client.SendMessageAsync(UrlFilaBilling, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo"), ordemServicoId);

        Assert.Same(falha, await Assert.ThrowsAsync<HttpRequestException>(action));
        Assert.Equal(UrlFilaOsService, Assert.Single(envios).UrlFila);
        var publicado = Assert.Single(ObterRegistrosDeLog(loggerMock, OrdemServicoLogEventos.EventoPublicado));
        Assert.Equal(FilaOsService, publicado.Propriedades["Fila"]);
        var falhaRegistrada = Assert.Single(ObterRegistrosDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha));
        Assert.Equal(FilaBilling, falhaRegistrada.Propriedades["Fila"]);
    }

    [Fact]
    public async Task PublicarAsync_DeveTentarResolverUrlNovamente_QuandoPrimeiraTentativaFalhar()
    {
        var publisher = CriarPublisher();
        var envios = CapturarEnvios();
        var ordemServicoId = Guid.NewGuid();
        _sqsClientMock
            .SetupSequence(client => client.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Fila inexistente."))
            .ReturnsAsync(UrlFilaOsService);

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo"), ordemServicoId));
        await publisher.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, "Motivo"), ordemServicoId);

        _sqsClientMock.Verify(client => client.GetQueueUrlAsync(FilaOsService, It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal([UrlFilaOsService, UrlFilaBilling], envios.Select(envio => envio.UrlFila));
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

        var action = async () => await publisher.PublicarAsync(new OrdemServicoRejeitada(Guid.NewGuid(), "Motivo"), Guid.NewGuid(), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.Empty(ObterRegistrosDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha));
    }
}
