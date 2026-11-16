using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Clientes;
using TechChallenge.Oficina.Entities.Features.Clientes.VOs;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.Entities.Features.Veiculos;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Commands;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using TechChallenge.Oficina.UseCases.Features.Servicos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Veiculos.UseCases;
using Xunit;

namespace TechChallenge.Oficina.UseCases.Tests.Features.OrdensServico.UseCases;

public sealed class OrdemServicoReferenciaUseCasesTests
{
    private readonly Mock<IOrdemServicoReferenciaGateway> _referenciaGatewayMock = new();
    private readonly Mock<IOrcamentoGateway> _orcamentoGatewayMock = new();
    private readonly Mock<IClienteGateway> _clienteGatewayMock = new();
    private readonly Mock<IVeiculoGateway> _veiculoGatewayMock = new();
    private readonly Mock<IServicoGateway> _servicoGatewayMock = new();
    private readonly Mock<IEventosPublisher> _eventosPublisherMock = new();
    private readonly Mock<IOrdemServicoStatusEmailSender> _statusEmailSenderMock = new();
    private readonly Mock<IOrdemServicoReferenciaUseCasesFacade> _facadeMock = new();

    private OrdemServicoReferenciaUseCases CriarUseCases(ILogger<OrdemServicoReferenciaUseCases>? logger = null)
    {
        _facadeMock.SetupGet(f => f.EventosPublisher).Returns(_eventosPublisherMock.Object);
        _facadeMock.SetupGet(f => f.OrdemServicoStatusEmailSender).Returns(_statusEmailSenderMock.Object);

        return new OrdemServicoReferenciaUseCases(
            _referenciaGatewayMock.Object,
            _orcamentoGatewayMock.Object,
            _clienteGatewayMock.Object,
            _veiculoGatewayMock.Object,
            _servicoGatewayMock.Object,
            _facadeMock.Object,
            logger ?? NullLogger<OrdemServicoReferenciaUseCases>.Instance);
    }

    private static (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeLog(Mock<ILogger<OrdemServicoReferenciaUseCases>> loggerMock, EventId evento)
    {
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == evento.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    private Cliente CriarCliente(string identificacao = "52998224725", string? email = null)
    {
        var cliente = Cliente.Criar("Cliente Teste", IdentificacaoCliente.Criar(identificacao), email);
        _clienteGatewayMock.Setup(g => g.ObterPorIdAsync(cliente.Id, It.IsAny<CancellationToken>())).ReturnsAsync(cliente);
        return cliente;
    }

    private Veiculo CriarVeiculo(Guid clienteId)
    {
        var veiculo = Veiculo.Criar("ABC1D23", "Toyota", "Corolla", 2023, "12345678901", clienteId);
        _veiculoGatewayMock.Setup(g => g.ObterPorIdAsync(veiculo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(veiculo);
        return veiculo;
    }

    private Servico CriarServico(string nome = "Revisao")
    {
        var servico = Servico.Criar(nome, "Servico completo", []);
        _servicoGatewayMock.Setup(g => g.ObterPorIdAsync(servico.Id, It.IsAny<CancellationToken>())).ReturnsAsync(servico);
        return servico;
    }

    private OrdemServicoReferencia CriarReferenciaExistente(Guid clienteId, Guid veiculoId, Guid servicoId, StatusOrdemServico status = StatusOrdemServico.Recebida)
    {
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), clienteId, veiculoId, [servicoId]);
        referencia.AtualizarStatus(status);
        _referenciaGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(referencia);
        return referencia;
    }

    private static OrdemServicoStatusAlterado CriarStatusAlterado(OrdemServicoReferencia referencia, StatusOrdemServico statusNovo)
        => new(referencia.Id, referencia.ClienteId, (int)referencia.Status, (int)statusNovo);

    private void VerificarRejeicaoPublicada(Guid ordemServicoId, string motivo)
    {
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, motivo), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
        _referenciaGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DeveAdicionarReferenciaRecebida_QuandoDadosValidos()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var veiculo = CriarVeiculo(cliente.Id);
        var servico = CriarServico();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [servico.Id, servico.Id] };

        var resultado = await useCases.RegistrarAsync(command);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.AdicionarAsync(
            It.Is<OrdemServicoReferencia>(r => r.Id == command.OrdemServicoId && r.ClienteId == cliente.Id && r.VeiculoId == veiculo.Id && r.Status == StatusOrdemServico.Recebida && r.ServicoIds.Single() == servico.Id),
            It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoRejeitada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DeveAtualizarReferenciaExistenteMantendoStatus_QuandoOrdemAtualizada()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var veiculo = CriarVeiculo(cliente.Id);
        var servicoAtual = CriarServico();
        var servicoNovo = CriarServico("Alinhamento");
        var referencia = CriarReferenciaExistente(cliente.Id, veiculo.Id, servicoAtual.Id, StatusOrdemServico.EmDiagnostico);
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = referencia.Id, ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [servicoNovo.Id] };

        var resultado = await useCases.RegistrarAsync(command);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
        Assert.Equal([servicoNovo.Id], referencia.ServicoIds);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        _referenciaGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DevePublicarRejeicaoERegistrarFalha_QuandoClienteNaoExiste()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var clienteId = Guid.NewGuid();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = clienteId, VeiculoId = Guid.NewGuid(), ServicoIds = [Guid.NewGuid()] };

        var resultado = await useCases.RegistrarAsync(command);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        VerificarRejeicaoPublicada(command.OrdemServicoId, $"Cliente com ID '{clienteId}' nao encontrado.");
        var (nivel, propriedades, excecao) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsFalha);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Null(excecao);
        Assert.Equal("abertura", propriedades["Etapa"]);
        Assert.Equal("nao_encontrado", propriedades["TipoFalha"]);
        Assert.Equal(command.OrdemServicoId, propriedades["OrdemServicoId"]);
    }

    [Fact]
    public async Task RegistrarAsync_DevePublicarRejeicao_QuandoVeiculoNaoExiste()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var veiculoId = Guid.NewGuid();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = cliente.Id, VeiculoId = veiculoId, ServicoIds = [Guid.NewGuid()] };

        await useCases.RegistrarAsync(command);

        VerificarRejeicaoPublicada(command.OrdemServicoId, $"Veiculo com ID '{veiculoId}' nao encontrado.");
    }

    [Fact]
    public async Task RegistrarAsync_DevePublicarRejeicaoDeRegraDeNegocio_QuandoVeiculoPertenceAOutroCliente()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente();
        var outroCliente = CriarCliente("04252011000110");
        var veiculoDeOutroCliente = CriarVeiculo(outroCliente.Id);
        var servico = CriarServico();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = cliente.Id, VeiculoId = veiculoDeOutroCliente.Id, ServicoIds = [servico.Id] };

        await useCases.RegistrarAsync(command);

        VerificarRejeicaoPublicada(command.OrdemServicoId, "O veiculo informado deve estar vinculado ao cliente da ordem de servico.");
        var (_, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsFalha);
        Assert.Equal("regra_negocio", propriedades["TipoFalha"]);
    }

    [Fact]
    public async Task RegistrarAsync_DevePublicarRejeicao_QuandoServicoNaoExiste()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var veiculo = CriarVeiculo(cliente.Id);
        var servico = CriarServico();
        var servicoInexistenteId = Guid.NewGuid();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [servico.Id, servicoInexistenteId] };

        await useCases.RegistrarAsync(command);

        VerificarRejeicaoPublicada(command.OrdemServicoId, $"Servico com ID '{servicoInexistenteId}' nao encontrado.");
    }

    [Fact]
    public async Task RegistrarAsync_DevePublicarRejeicao_QuandoNaoInformarServicos()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var veiculo = CriarVeiculo(cliente.Id);
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [] };

        await useCases.RegistrarAsync(command);

        VerificarRejeicaoPublicada(command.OrdemServicoId, "A ordem de servico deve possuir ao menos um servico.");
    }

    [Fact]
    public async Task RegistrarAsync_DeveRemoverReferenciaExistenteEPublicarRejeicao_QuandoAtualizacaoInvalida()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente();
        var veiculo = CriarVeiculo(cliente.Id);
        var servico = CriarServico();
        var referencia = CriarReferenciaExistente(cliente.Id, veiculo.Id, servico.Id);
        var servicoInexistenteId = Guid.NewGuid();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = referencia.Id, ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [servicoInexistenteId] };

        var resultado = await useCases.RegistrarAsync(command);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        VerificarRejeicaoPublicada(referencia.Id, $"Servico com ID '{servicoInexistenteId}' nao encontrado.");
        var (_, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsFalha);
        Assert.Equal("atualizacao", propriedades["Etapa"]);
    }

    [Fact]
    public async Task RegistrarAsync_DeveLancarDomainExceptionSemPublicar_QuandoOrdemServicoSemIdentificador()
    {
        var useCases = CriarUseCases();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.Empty, ClienteId = Guid.NewGuid(), VeiculoId = Guid.NewGuid(), ServicoIds = [Guid.NewGuid()] };

        var action = async () => await useCases.RegistrarAsync(command);

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("O identificador da ordem de servico e obrigatorio.", exception.Message);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoRejeitada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtualizarStatusAsync_DeveRetornarIgnorado_QuandoReferenciaNaoExiste()
    {
        var useCases = CriarUseCases();

        var resultado = await useCases.AtualizarStatusAsync(new OrdemServicoStatusAlterado(Guid.NewGuid(), Guid.NewGuid(), 1, 2));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        _statusEmailSenderMock.Verify(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtualizarStatusAsync_DeveRetornarIgnorado_QuandoStatusNaoMudou()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente(email: "cliente@teste.com");
        var referencia = CriarReferenciaExistente(cliente.Id, Guid.NewGuid(), Guid.NewGuid(), StatusOrdemServico.EmDiagnostico);

        var resultado = await useCases.AtualizarStatusAsync(CriarStatusAlterado(referencia, StatusOrdemServico.EmDiagnostico));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        _statusEmailSenderMock.Verify(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    public async Task AtualizarStatusAsync_DeveAtualizarStatusEEnviarEmail_QuandoStatusNotificavel(StatusOrdemServico statusNovo)
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente(email: "cliente@teste.com");
        var servico = CriarServico("Troca de oleo");
        var referencia = CriarReferenciaExistente(cliente.Id, Guid.NewGuid(), servico.Id);
        var orcamento = Orcamento.Criar(referencia.Id, [servico]);
        _orcamentoGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(orcamento);
        NotificacaoStatusOrdemServico? notificacaoEnviada = null;
        _statusEmailSenderMock
            .Setup(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), "cliente@teste.com", It.IsAny<CancellationToken>()))
            .Callback<NotificacaoStatusOrdemServico, string, CancellationToken>((notificacao, _, _) => notificacaoEnviada = notificacao)
            .Returns(Task.CompletedTask);

        var resultado = await useCases.AtualizarStatusAsync(CriarStatusAlterado(referencia, statusNovo));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(statusNovo, referencia.Status);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(notificacaoEnviada);
        Assert.Equal(referencia.Id, notificacaoEnviada!.OrdemServicoId);
        Assert.Equal(referencia.ClienteId, notificacaoEnviada.ClienteId);
        Assert.Equal(referencia.VeiculoId, notificacaoEnviada.VeiculoId);
        Assert.Equal(statusNovo, notificacaoEnviada.StatusNovo);
        Assert.Equal(orcamento.ValorTotal, notificacaoEnviada.ValorOrcamento);
        Assert.Equal(["Troca de oleo"], notificacaoEnviada.Servicos);
    }

    [Theory]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    [InlineData(StatusOrdemServico.Encerrada)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public async Task AtualizarStatusAsync_DeveAtualizarSemEnviarEmail_QuandoStatusNaoNotificavel(StatusOrdemServico statusNovo)
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente(email: "cliente@teste.com");
        var referencia = CriarReferenciaExistente(cliente.Id, Guid.NewGuid(), Guid.NewGuid());

        var resultado = await useCases.AtualizarStatusAsync(CriarStatusAlterado(referencia, statusNovo));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(statusNovo, referencia.Status);
        _statusEmailSenderMock.Verify(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtualizarStatusAsync_DeveAtualizarSemEnviarEmail_QuandoClienteSemEmail()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var referencia = CriarReferenciaExistente(cliente.Id, Guid.NewGuid(), Guid.NewGuid());

        var resultado = await useCases.AtualizarStatusAsync(CriarStatusAlterado(referencia, StatusOrdemServico.EmDiagnostico));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
        _statusEmailSenderMock.Verify(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtualizarStatusAsync_DeveRegistrarFalhaDeIntegracaoSemInterromper_QuandoEmailFalhar()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente(email: "cliente@teste.com");
        var referencia = CriarReferenciaExistente(cliente.Id, Guid.NewGuid(), Guid.NewGuid());
        var falha = new HttpRequestException("Falha de comunicacao com o provedor de email.");
        _statusEmailSenderMock
            .Setup(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), "cliente@teste.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var resultado = await useCases.AtualizarStatusAsync(CriarStatusAlterado(referencia, StatusOrdemServico.EmDiagnostico));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        var (nivel, propriedades, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.IntegracaoFalha);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Same(falha, excecaoRegistrada);
        Assert.Equal("resend", propriedades["Integracao"]);
        Assert.Equal("notificacao_status", propriedades["Operacao"]);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
        Assert.Equal("em_diagnostico", propriedades["StatusNovo"]);
    }

    [Fact]
    public async Task AtualizarStatusAsync_DeveRegistrarAvisoSemEventoSemInterromper_QuandoBuscaDoClienteFalhar()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var falha = new InvalidOperationException("Falha ao consultar o cliente.");
        _clienteGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.ClienteId, It.IsAny<CancellationToken>())).ThrowsAsync(falha);

        var resultado = await useCases.AtualizarStatusAsync(CriarStatusAlterado(referencia, StatusOrdemServico.EmExecucao));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        var (nivel, propriedades, excecaoRegistrada) = ObterRegistroDeLog(loggerMock, default);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Same(falha, excecaoRegistrada);
        Assert.False(propriedades.ContainsKey("Integracao"));
        _statusEmailSenderMock.Verify(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AtualizarStatusAsync_DevePropagarCancelamento_QuandoCanceladoDuranteNotificacao()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var cliente = CriarCliente(email: "cliente@teste.com");
        var referencia = CriarReferenciaExistente(cliente.Id, Guid.NewGuid(), Guid.NewGuid());
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _statusEmailSenderMock
            .Setup(s => s.EnviarStatusAlteradoAsync(It.IsAny<NotificacaoStatusOrdemServico>(), "cliente@teste.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await useCases.AtualizarStatusAsync(CriarStatusAlterado(referencia, StatusOrdemServico.Finalizada), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.DoesNotContain(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
    }

    [Fact]
    public async Task AtualizarStatusAsync_DeveLancarDomainException_QuandoStatusInvalido()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var action = async () => await useCases.AtualizarStatusAsync(new OrdemServicoStatusAlterado(referencia.Id, referencia.ClienteId, 1, 99));

        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal("O status da ordem de servico informado e invalido.", exception.Message);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoverAsync_DeveRemoverReferenciaEOrcamento_QuandoExistem()
    {
        var useCases = CriarUseCases();
        var servico = Servico.Criar("Revisao", "Servico completo", []);
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servico.Id);
        var orcamento = Orcamento.Criar(referencia.Id, [servico]);
        _orcamentoGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(orcamento);

        var resultado = await useCases.RemoverAsync(new OrdemServicoExcluida(referencia.Id));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _orcamentoGatewayMock.Verify(g => g.RemoverAsync(orcamento, It.IsAny<CancellationToken>()), Times.Once);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoverAsync_DeveRemoverSomenteReferencia_QuandoNaoHaOrcamento()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var resultado = await useCases.RemoverAsync(new OrdemServicoExcluida(referencia.Id));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        _orcamentoGatewayMock.Verify(g => g.RemoverAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoverAsync_DeveRetornarIgnorado_QuandoNadaExiste()
    {
        var useCases = CriarUseCases();

        var resultado = await useCases.RemoverAsync(new OrdemServicoExcluida(Guid.NewGuid()));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        _orcamentoGatewayMock.Verify(g => g.RemoverAsync(It.IsAny<Orcamento>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
