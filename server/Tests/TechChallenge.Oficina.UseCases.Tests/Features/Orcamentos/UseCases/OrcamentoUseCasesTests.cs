using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Clientes;
using TechChallenge.Oficina.Entities.Features.Clientes.VOs;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.UseCases.Features.Servicos.UseCases;
using Xunit;

namespace TechChallenge.Oficina.UseCases.Tests.Features.Orcamentos.UseCases;

public sealed class OrcamentoUseCasesTests
{
    private static readonly IMapper Mapper = CriarMapper();

    private readonly Mock<IOrcamentoGateway> _orcamentoGatewayMock = new();
    private readonly Mock<IOrdemServicoReferenciaGateway> _referenciaGatewayMock = new();
    private readonly Mock<IClienteGateway> _clienteGatewayMock = new();
    private readonly Mock<IServicoGateway> _servicoGatewayMock = new();
    private readonly Mock<IEstoqueUseCases> _estoqueUseCasesMock = new();
    private readonly Mock<IOrcamentoEmailSender> _orcamentoEmailSenderMock = new();
    private readonly Mock<IEventosPublisher> _eventosPublisherMock = new();
    private readonly Mock<IOrcamentoUseCasesFacade> _facadeMock = new();

    private static IMapper CriarMapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(OrcamentoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IMapper>();
    }

    private OrcamentoUseCases CriarUseCases(ILogger<OrcamentoUseCases>? logger = null)
    {
        _facadeMock.SetupGet(f => f.EstoqueUseCases).Returns(_estoqueUseCasesMock.Object);
        _facadeMock.SetupGet(f => f.OrcamentoEmailSender).Returns(_orcamentoEmailSenderMock.Object);
        _facadeMock.SetupGet(f => f.EventosPublisher).Returns(_eventosPublisherMock.Object);

        return new OrcamentoUseCases(
            Mapper,
            _orcamentoGatewayMock.Object,
            _referenciaGatewayMock.Object,
            _clienteGatewayMock.Object,
            _servicoGatewayMock.Object,
            _facadeMock.Object,
            logger ?? NullLogger<OrcamentoUseCases>.Instance);
    }

    private static (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeLog(Mock<ILogger<OrcamentoUseCases>> loggerMock, EventId evento)
    {
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    private static Cliente CriarCliente(string? email = null) => Cliente.Criar("Cliente Teste", IdentificacaoCliente.Criar("52998224725"), email);

    private static Servico CriarServico(decimal valorUnitario = 25m, int quantidade = 2)
    {
        var insumo = Insumo.Criar("Oleo", "Fabricante", 10, valorUnitario);
        return Servico.Criar("Troca de oleo", "Servico completo", [ItemServico.Criar(insumo, quantidade)]);
    }

    private OrdemServicoReferencia CriarReferencia(Guid clienteId, Servico servico, StatusOrdemServico status = StatusOrdemServico.Recebida)
    {
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), clienteId, Guid.NewGuid(), [servico.Id]);
        referencia.AtualizarStatus(status);

        _referenciaGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(referencia);
        _servicoGatewayMock.Setup(g => g.ObterPorIdAsync(servico.Id, It.IsAny<CancellationToken>())).ReturnsAsync(servico);

        return referencia;
    }

    private Orcamento CriarOrcamento(OrdemServicoReferencia referencia, Servico servico)
    {
        var orcamento = Orcamento.Criar(referencia.Id, [servico], new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));
        _orcamentoGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(orcamento);
        return orcamento;
    }

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveRetornarViewModel_QuandoOrcamentoExiste()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico);
        CriarOrcamento(referencia, servico);

        var resultado = await useCases.ObterPorOrdemServicoIdAsync(new ObterOrcamentoPorOrdemServicoIdQuery { OrdemServicoId = referencia.Id });

        Assert.Equal(referencia.Id, resultado.OrdemServicoId);
        Assert.Equal(50m, resultado.ValorTotal);
        Assert.Equal((int)StatusOrcamento.Gerado, resultado.Status);
        Assert.Equal("Gerado", resultado.StatusDescricao);
        Assert.Single(resultado.Servicos);
    }

    [Fact]
    public async Task ObterPorOrdemServicoIdAsync_DeveLancarKeyNotFoundException_QuandoOrcamentoNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.ObterPorOrdemServicoIdAsync(new ObterOrcamentoPorOrdemServicoIdQuery { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Orcamento nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task GerarAsync_DeveCriarOrcamentoPublicarEventoERegistrarLog()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.EmDiagnostico);

        var resultado = await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal(referencia.Id, resultado.OrdemServicoId);
        Assert.Equal(50m, resultado.ValorTotal);
        Assert.Equal((int)StatusOrcamento.Gerado, resultado.Status);
        _estoqueUseCasesMock.Verify(e => e.VerificarDisponibilidadeParaOrcamentoAsync(It.Is<IReadOnlyCollection<Servico>>(s => s.Single() == servico), It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.AdicionarAsync(It.Is<Orcamento>(o => o.Id == referencia.Id && o.ValorTotal == 50m), It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoGerado(referencia.Id, 50m), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsOrcamentoGerado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
        Assert.Equal(false, propriedades["Regerado"]);
    }

    [Fact]
    public async Task GerarAsync_DeveRegerarOrcamentoExistenteComValoresAtuais()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia, servico);
        servico.ItensServico.Single().Insumo.AtualizarValorUnitario(40m);

        var resultado = await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal(80m, resultado.ValorTotal);
        Assert.Equal(80m, orcamento.ValorTotal);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoGerado(referencia.Id, 80m), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
        var (_, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsOrcamentoGerado);
        Assert.Equal(true, propriedades["Regerado"]);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarDomainExceptionSemPersistir_QuandoEstoqueInsuficiente()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico);
        _estoqueUseCasesMock
            .Setup(e => e.VerificarDisponibilidadeParaOrcamentoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Estoque insuficiente para gerar orcamento."));

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Estoque insuficiente para gerar orcamento.", exception.Message);
        _orcamentoGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarKeyNotFoundException_QuandoOrdemNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Ordem de servico nao encontrada.", exception.Message);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarDomainExceptionSemVerificarEstoque_QuandoOrdemFinalizada()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.Finalizada);

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento gerado.", exception.Message);
        _estoqueUseCasesMock.Verify(e => e.VerificarDisponibilidadeParaOrcamentoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarDomainExceptionSemVerificarEstoque_QuandoOrcamentoJaFoiAprovado()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia, servico).Aprovar();

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser gerado novamente.", exception.Message);
        _estoqueUseCasesMock.Verify(e => e.VerificarDisponibilidadeParaOrcamentoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GerarAsync_DeveLancarKeyNotFoundException_QuandoServicoDaOrdemNaoExisteMais()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico);
        _servicoGatewayMock.Setup(g => g.ObterPorIdAsync(servico.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Servico?)null);

        var action = async () => await useCases.GerarAsync(new GerarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal($"Servico com ID '{servico.Id}' nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveEnviarERegistrarLog_QuandoOrcamentoEEmailExistem()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente("cliente@teste.com");
        var servico = CriarServico();
        var referencia = CriarReferencia(cliente.Id, servico);
        var orcamento = CriarOrcamento(referencia, servico);
        _clienteGatewayMock.Setup(g => g.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);

        await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id });

        _orcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsOrcamentoEnviado);
        Assert.Equal(LogLevel.Information, nivel);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveLancarKeyNotFoundException_QuandoOrcamentoNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Orcamento nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveLancarDomainException_QuandoClienteSemEmail()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var servico = CriarServico();
        var referencia = CriarReferencia(cliente.Id, servico);
        CriarOrcamento(referencia, servico);
        _clienteGatewayMock.Setup(g => g.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("O cliente da ordem de servico nao possui email cadastrado.", exception.Message);
        _orcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(It.IsAny<Orcamento>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoEnvioFalhar()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente("cliente@teste.com");
        var servico = CriarServico();
        var referencia = CriarReferencia(cliente.Id, servico);
        var orcamento = CriarOrcamento(referencia, servico);
        var falha = new HttpRequestException("Falha de comunicacao com o provedor de email.");
        _clienteGatewayMock.Setup(g => g.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);
        _orcamentoEmailSenderMock.Setup(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Same(falha, exception);
        var (nivel, propriedades, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecaoRegistrada);
        Assert.Equal("resend", propriedades["Integracao"]);
        Assert.Equal("envio_orcamento", propriedades["Operacao"]);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveRegistrarFalhaDeIntegracaoERelancar_QuandoEnvioExpirarSemCancelamento()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente("cliente@teste.com");
        var servico = CriarServico();
        var referencia = CriarReferencia(cliente.Id, servico);
        var orcamento = CriarOrcamento(referencia, servico);
        var timeout = new TaskCanceledException("Timeout do HttpClient no envio de email", new TimeoutException("Tempo esgotado"));
        _clienteGatewayMock.Setup(g => g.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);
        _orcamentoEmailSenderMock.Setup(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", It.IsAny<CancellationToken>())).ThrowsAsync(timeout);

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<TaskCanceledException>(action);
        Assert.Same(timeout, exception);
        var (nivel, _, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(timeout, excecaoRegistrada);
    }

    [Fact]
    public async Task EnviarPorEmailAsync_DeveRelancarSemRegistrarFalhaDeIntegracao_QuandoRequisicaoCancelada()
    {
        var loggerMock = new Mock<ILogger<OrcamentoUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente("cliente@teste.com");
        var servico = CriarServico();
        var referencia = CriarReferencia(cliente.Id, servico);
        var orcamento = CriarOrcamento(referencia, servico);
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _clienteGatewayMock.Setup(g => g.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);
        _orcamentoEmailSenderMock.Setup(s => s.EnviarOrcamentoAsync(orcamento, "cliente@teste.com", It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await useCases.EnviarPorEmailAsync(new EnviarOrcamentoCommand { OrdemServicoId = referencia.Id }, cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
    }

    [Fact]
    public async Task AprovarAsync_DeveDebitarEstoqueAprovarEPublicarEvento()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia, servico);

        var resultado = await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal((int)StatusOrcamento.Aprovado, resultado.Status);
        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
        _estoqueUseCasesMock.Verify(e => e.VerificarDisponibilidadeParaOrcamentoAsync(It.Is<IReadOnlyCollection<Servico>>(s => s.Single() == servico), It.IsAny<CancellationToken>()), Times.Once);
        _estoqueUseCasesMock.Verify(e => e.DebitarEstoqueParaOrdemServicoAsync(It.Is<IReadOnlyCollection<Servico>>(s => s.Single() == servico), It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoAprovado(referencia.Id), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AprovarAsync_DevePermitirAprovacao_QuandoOrdemAindaNaoRefletiuOrcamentoGerado()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.EmDiagnostico);
        CriarOrcamento(referencia, servico);

        var resultado = await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal((int)StatusOrcamento.Aprovado, resultado.Status);
    }

    [Fact]
    public async Task AprovarAsync_DeveLancarDomainExceptionSemDebitar_QuandoEstoqueInsuficienteNaAprovacao()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia, servico);
        _estoqueUseCasesMock
            .Setup(e => e.VerificarDisponibilidadeParaOrcamentoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Estoque insuficiente para aprovar orcamento."));

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Estoque insuficiente para aprovar orcamento.", exception.Message);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
        _estoqueUseCasesMock.Verify(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoAprovado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AprovarAsync_DeveLancarDomainExceptionSemMovimentarEstoque_QuandoOrcamentoJaFoiAprovado()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia, servico).Aprovar();

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser aprovado.", exception.Message);
        _estoqueUseCasesMock.Verify(e => e.VerificarDisponibilidadeParaOrcamentoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
        _estoqueUseCasesMock.Verify(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AprovarAsync_DeveLancarDomainExceptionSemMovimentarEstoque_QuandoOrdemJaEstaEmExecucao()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.EmExecucao);
        CriarOrcamento(referencia, servico);

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento aprovado.", exception.Message);
        _estoqueUseCasesMock.Verify(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AprovarAsync_DeveLancarKeyNotFoundException_QuandoOrcamentoNaoExiste()
    {
        var useCases = CriarUseCases();

        var action = async () => await useCases.AprovarAsync(new AprovarOrcamentoCommand { OrdemServicoId = Guid.NewGuid() });

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Orcamento nao encontrado.", exception.Message);
    }

    [Fact]
    public async Task RecusarAsync_DeveRecusarEPublicarEventoSemMovimentarEstoque()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.AguardandoAprovacao);
        var orcamento = CriarOrcamento(referencia, servico);

        var resultado = await useCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = referencia.Id });

        Assert.Equal((int)StatusOrcamento.Recusado, resultado.Status);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoRecusado(referencia.Id), referencia.Id, It.IsAny<CancellationToken>()), Times.Once);
        _estoqueUseCasesMock.Verify(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecusarAsync_DeveLancarDomainException_QuandoOrcamentoJaFoiRecusado()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.AguardandoAprovacao);
        CriarOrcamento(referencia, servico).Recusar();

        var action = async () => await useCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser recusado.", exception.Message);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoRecusado>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecusarAsync_DeveLancarDomainException_QuandoOrdemFinalizada()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico();
        var referencia = CriarReferencia(Guid.NewGuid(), servico, StatusOrdemServico.Finalizada);
        var orcamento = CriarOrcamento(referencia, servico);

        var action = async () => await useCases.RecusarAsync(new RecusarOrcamentoCommand { OrdemServicoId = referencia.Id });

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento recusado.", exception.Message);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
        _orcamentoGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
