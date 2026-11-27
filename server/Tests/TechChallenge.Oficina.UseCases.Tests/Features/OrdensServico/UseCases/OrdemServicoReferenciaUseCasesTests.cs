using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Clientes;
using TechChallenge.Oficina.Entities.Features.Clientes.VOs;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.Entities.Features.Veiculos;
using TechChallenge.Oficina.UseCases.Features.Clientes.UseCases;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;
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
    private readonly Mock<IClienteGateway> _clienteGatewayMock = new();
    private readonly Mock<IVeiculoGateway> _veiculoGatewayMock = new();
    private readonly Mock<IServicoGateway> _servicoGatewayMock = new();
    private readonly Mock<IEstoqueUseCases> _estoqueUseCasesMock = new();
    private readonly Mock<IEventosPublisher> _eventosPublisherMock = new();
    private readonly Mock<IOrdemServicoStatusEmailSender> _statusEmailSenderMock = new();
    private readonly Mock<IHistoricoEstoqueGateway> _historicoEstoqueGatewayMock = new();
    private readonly Mock<IOrdemServicoReferenciaUseCasesFacade> _facadeMock = new();
    private bool _emTransacao;

    private OrdemServicoReferenciaUseCases CriarUseCases(ILogger<OrdemServicoReferenciaUseCases>? logger = null)
    {
        _facadeMock.SetupGet(f => f.EstoqueUseCases).Returns(_estoqueUseCasesMock.Object);
        _facadeMock.SetupGet(f => f.EventosPublisher).Returns(_eventosPublisherMock.Object);
        _facadeMock.SetupGet(f => f.OrdemServicoStatusEmailSender).Returns(_statusEmailSenderMock.Object);
        _facadeMock.SetupGet(f => f.HistoricoEstoqueGateway).Returns(_historicoEstoqueGatewayMock.Object);
        // Debito sem insumos por padrao (o Moq devolveria null para a colecao); os testes do debito configuram os insumos debitados
        _estoqueUseCasesMock
            .Setup(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _referenciaGatewayMock
            .Setup(g => g.ExecutarEmTransacaoAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Task>, CancellationToken>(async (operacao, _) =>
            {
                _emTransacao = true;
                try
                {
                    await operacao();
                }
                finally
                {
                    _emTransacao = false;
                }
            });

        return new OrdemServicoReferenciaUseCases(
            _referenciaGatewayMock.Object,
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

    private Servico CriarServico(string nome = "Revisao", params (int Quantidade, decimal ValorUnitario)[] itens)
    {
        var itensServico = itens
            .Select((item, indice) => ItemServico.Criar(Insumo.Criar($"Insumo {indice + 1}", "Fabricante", 10, item.ValorUnitario), item.Quantidade))
            .ToArray();
        var servico = Servico.Criar(nome, "Servico completo", itensServico);
        _servicoGatewayMock.Setup(g => g.ObterPorIdAsync(servico.Id, It.IsAny<CancellationToken>())).ReturnsAsync(servico);
        return servico;
    }

    private OrdemServicoReferencia CriarReferenciaExistente(Guid clienteId, Guid veiculoId, Guid servicoId, StatusOrdemServico status = StatusOrdemServico.Recebida)
        => CriarReferenciaExistente(clienteId, veiculoId, [servicoId], status);

    private OrdemServicoReferencia CriarReferenciaExistente(Guid clienteId, Guid veiculoId, IReadOnlyCollection<Guid> servicoIds, StatusOrdemServico status = StatusOrdemServico.Recebida)
    {
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), clienteId, veiculoId, servicoIds);
        referencia.AtualizarStatus(status);
        _referenciaGatewayMock.Setup(g => g.ObterPorIdAsync(referencia.Id, It.IsAny<CancellationToken>())).ReturnsAsync(referencia);
        return referencia;
    }

    private static OrdemServicoStatusAlterado CriarStatusAlterado(OrdemServicoReferencia referencia, StatusOrdemServico statusNovo)
        => new(referencia.Id, referencia.ClienteId, (int)referencia.Status, (int)statusNovo);

    private void ConfigurarEstoqueSuficiente(bool estoqueSuficiente)
    {
        _estoqueUseCasesMock
            .Setup(e => e.PossuiDisponibilidadeAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(estoqueSuficiente);
    }

    private Func<OrdemServicoValidada> CapturarValidacaoPublicada()
    {
        OrdemServicoValidada? eventoPublicado = null;

        _eventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<OrdemServicoValidada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<OrdemServicoValidada, Guid, CancellationToken>((evento, _, _) => eventoPublicado = evento)
            .Returns(Task.CompletedTask);

        return () => eventoPublicado ?? throw new InvalidOperationException("Nenhum evento OrdemServicoValidada foi publicado.");
    }

    private void VerificarRejeicaoPublicada(Guid ordemServicoId, string motivo)
    {
        _eventosPublisherMock.Verify(p => p.PublicarAsync(new OrdemServicoRejeitada(ordemServicoId, motivo), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoValidada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
            It.Is<OrdemServicoReferencia>(r => r.Id == command.OrdemServicoId && r.ClienteId == cliente.Id && r.VeiculoId == veiculo.Id && r.Status == StatusOrdemServico.Recebida && r.ServicoIds.Single() == servico.Id && !r.EstoqueDebitado),
            It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoValidada>(), command.OrdemServicoId, It.IsAny<CancellationToken>()), Times.Once);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoRejeitada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DevePublicarOrdemServicoValidadaComServicosPrecificados_QuandoDadosValidos()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente(email: "cliente@teste.com");
        var veiculo = CriarVeiculo(cliente.Id);
        var trocaDeOleo = CriarServico("Troca de oleo", (2, 15.50m), (1, 10m));
        var alinhamento = CriarServico("Alinhamento", (1, 30m));
        ConfigurarEstoqueSuficiente(true);
        var eventoPublicado = CapturarValidacaoPublicada();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [trocaDeOleo.Id, alinhamento.Id, trocaDeOleo.Id] };

        await useCases.RegistrarAsync(command);

        var evento = eventoPublicado();
        Assert.Equal(command.OrdemServicoId, evento.OrdemServicoId);
        Assert.Equal(cliente.Id, evento.ClienteId);
        Assert.Equal(veiculo.Id, evento.VeiculoId);
        Assert.Equal("cliente@teste.com", evento.ClienteEmail);
        Assert.True(evento.EstoqueSuficiente);
        Assert.Equal(
            [new ServicoValidado(trocaDeOleo.Id, "Troca de oleo", 41m), new ServicoValidado(alinhamento.Id, "Alinhamento", 30m)],
            evento.Servicos);
        Assert.Equal(71m, evento.ValorTotal);
        _estoqueUseCasesMock.Verify(e => e.PossuiDisponibilidadeAsync(
            It.Is<IReadOnlyCollection<Servico>>(servicos => servicos.Count == 2 && servicos.Contains(trocaDeOleo) && servicos.Contains(alinhamento)),
            It.IsAny<CancellationToken>()), Times.Once);
        _estoqueUseCasesMock.Verify(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DeveRegistrarEPublicarEstoqueInsuficienteSemEmail_QuandoNaoHaEstoqueEClienteSemEmail()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var veiculo = CriarVeiculo(cliente.Id);
        var servico = CriarServico("Troca de pastilha", (2, 50m));
        ConfigurarEstoqueSuficiente(false);
        var eventoPublicado = CapturarValidacaoPublicada();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = Guid.NewGuid(), ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [servico.Id] };

        var resultado = await useCases.RegistrarAsync(command);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.AdicionarAsync(It.Is<OrdemServicoReferencia>(r => r.Id == command.OrdemServicoId), It.IsAny<CancellationToken>()), Times.Once);
        var evento = eventoPublicado();
        Assert.False(evento.EstoqueSuficiente);
        Assert.Null(evento.ClienteEmail);
        Assert.Equal(100m, evento.ValorTotal);
        _eventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoRejeitada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_DeveAtualizarReferenciaExistenteMantendoStatusEPublicarValidacao_QuandoOrdemAtualizada()
    {
        var useCases = CriarUseCases();
        var cliente = CriarCliente();
        var veiculo = CriarVeiculo(cliente.Id);
        var servicoAtual = CriarServico();
        var servicoNovo = CriarServico("Alinhamento", (1, 30m));
        var referencia = CriarReferenciaExistente(cliente.Id, veiculo.Id, servicoAtual.Id, StatusOrdemServico.EmDiagnostico);
        var eventoPublicado = CapturarValidacaoPublicada();
        var command = new RegistrarOrdemServicoReferenciaCommand { OrdemServicoId = referencia.Id, ClienteId = cliente.Id, VeiculoId = veiculo.Id, ServicoIds = [servicoNovo.Id] };

        var resultado = await useCases.RegistrarAsync(command);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
        Assert.Equal([servicoNovo.Id], referencia.ServicoIds);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        _referenciaGatewayMock.Verify(g => g.AdicionarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal([new ServicoValidado(servicoNovo.Id, "Alinhamento", 30m)], eventoPublicado().Servicos);
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
        Assert.Empty(_eventosPublisherMock.Invocations);
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
    public async Task RemoverAsync_DeveRemoverReferencia_QuandoExiste()
    {
        var useCases = CriarUseCases();
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var resultado = await useCases.RemoverAsync(new OrdemServicoExcluida(referencia.Id));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoverAsync_DeveRetornarIgnorado_QuandoReferenciaNaoExiste()
    {
        var useCases = CriarUseCases();

        var resultado = await useCases.RemoverAsync(new OrdemServicoExcluida(Guid.NewGuid()));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        _referenciaGatewayMock.Verify(g => g.RemoverAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DeveDebitarEstoqueERegistrarDebito_QuandoReferenciaExiste()
    {
        var useCases = CriarUseCases();
        var trocaDeOleo = CriarServico("Troca de oleo", (2, 15.50m));
        var alinhamento = CriarServico("Alinhamento", (1, 30m));
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), [trocaDeOleo.Id, alinhamento.Id], StatusOrdemServico.AguardandoAprovacao);

        var resultado = await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 61m));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.True(referencia.EstoqueDebitado);
        _estoqueUseCasesMock.Verify(e => e.DebitarEstoqueParaOrdemServicoAsync(
            It.Is<IReadOnlyCollection<Servico>>(servicos => servicos.Count == 2 && servicos.Contains(trocaDeOleo) && servicos.Contains(alinhamento)),
            It.IsAny<CancellationToken>()), Times.Once);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_eventosPublisherMock.Invocations);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DeveDebitarERegistrarDebitoDentroDaMesmaTransacao()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico("Troca de oleo", (2, 15.50m));
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servico.Id);
        var debitoEmTransacao = false;
        var marcaEmTransacao = false;
        _estoqueUseCasesMock
            .Setup(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .Callback(() => debitoEmTransacao = _emTransacao)
            .ReturnsAsync([]);
        _referenciaGatewayMock
            .Setup(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()))
            .Callback(() => marcaEmTransacao = _emTransacao && referencia.EstoqueDebitado)
            .Returns(Task.CompletedTask);

        var resultado = await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 31m));

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.True(debitoEmTransacao);
        Assert.True(marcaEmTransacao);
        _referenciaGatewayMock.Verify(g => g.ExecutarEmTransacaoAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DeveRegistrarUmDebitoPorInsumoNoHistoricoDepoisDaTransacao()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico("Revisao", (2, 15.50m), (1, 30m));
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servico.Id, StatusOrdemServico.AguardandoAprovacao);
        var oleoId = Guid.NewGuid();
        var filtroId = Guid.NewGuid();
        _estoqueUseCasesMock
            .Setup(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DebitoInsumo(oleoId, 2, 10, 8), new DebitoInsumo(filtroId, 1, 5, 4)]);
        var registros = new List<(MovimentacaoEstoque Movimentacao, bool EmTransacao, bool EstoqueDebitado, CancellationToken Token)>();
        _historicoEstoqueGatewayMock
            .Setup(h => h.RegistrarAsync(It.IsAny<MovimentacaoEstoque>(), It.IsAny<CancellationToken>()))
            .Callback<MovimentacaoEstoque, CancellationToken>((movimentacao, token) => registros.Add((movimentacao, _emTransacao, referencia.EstoqueDebitado, token)))
            .Returns(Task.CompletedTask);
        using var cancelamento = new CancellationTokenSource();

        var resultado = await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 61m), cancelamento.Token);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Equal(2, registros.Count);
        // Fora da transacao (depois do debito e da marca na referencia), sem o token do consumidor e todas com o mesmo instante
        Assert.All(registros, registro =>
        {
            Assert.False(registro.EmTransacao);
            Assert.True(registro.EstoqueDebitado);
            Assert.Equal(CancellationToken.None, registro.Token);
            Assert.Equal("DebitoOrdemServico", registro.Movimentacao.Tipo);
            Assert.Equal("evento", registro.Movimentacao.Origem);
            Assert.Equal(referencia.Id, registro.Movimentacao.OrdemServicoId);
        });
        Assert.Single(registros.Select(registro => registro.Movimentacao.OcorridoEm).Distinct());
        var oleo = Assert.Single(registros, registro => registro.Movimentacao.InsumoId == oleoId).Movimentacao;
        Assert.Equal((-2, 10, 8), (oleo.Quantidade, oleo.SaldoAnterior, oleo.SaldoNovo));
        var filtro = Assert.Single(registros, registro => registro.Movimentacao.InsumoId == filtroId).Movimentacao;
        Assert.Equal((-1, 5, 4), (filtro.Quantidade, filtro.SaldoAnterior, filtro.SaldoNovo));
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_NaoDeveRegistrarNoHistorico_QuandoATransacaoFalhaDepoisDoDebito()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico("Troca de oleo", (2, 15.50m));
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servico.Id);
        _estoqueUseCasesMock
            .Setup(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DebitoInsumo(Guid.NewGuid(), 2, 10, 8)]);
        _referenciaGatewayMock
            .Setup(g => g.AtualizarAsync(referencia, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Banco indisponivel."));

        var action = async () => await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 31m));

        // A transacao e desfeita e a mensagem volta para a fila: o debito desfeito nao vai para o historico
        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DeveRetornarIgnorado_QuandoReferenciaNaoExiste()
    {
        var useCases = CriarUseCases();

        var resultado = await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(Guid.NewGuid(), Guid.NewGuid(), 100m));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.Empty(_estoqueUseCasesMock.Invocations);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DeveRetornarIgnoradoSemDebitarNovamente_QuandoEstoqueJaDebitado()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico("Troca de oleo", (2, 15.50m));
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servico.Id);
        referencia.RegistrarDebitoEstoque();

        var resultado = await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 31m));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.Empty(_estoqueUseCasesMock.Invocations);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DeveRegistrarFalhaERetornarIgnorado_QuandoEstoqueInsuficiente()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var servico = CriarServico("Troca de oleo", (2, 15.50m));
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servico.Id);
        _estoqueUseCasesMock
            .Setup(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Estoque insuficiente do insumo 'Insumo 1'. Disponivel: 1, Necessario: 2."));

        var resultado = await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 31m));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.False(referencia.EstoqueDebitado);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        var (nivel, propriedades, excecao) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsFalha);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Null(excecao);
        Assert.Equal("estoque_debito", propriedades["Etapa"]);
        Assert.Equal("regra_negocio", propriedades["TipoFalha"]);
        Assert.Equal(referencia.Id, propriedades["OrdemServicoId"]);
        Assert.Equal("Estoque insuficiente do insumo 'Insumo 1'. Disponivel: 1, Necessario: 2.", propriedades["Motivo"]);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DeveRegistrarFalhaNaoEncontrado_QuandoServicoDaOrdemNaoExisteMais()
    {
        var loggerMock = new Mock<ILogger<OrdemServicoReferenciaUseCases>>();
        var useCases = CriarUseCases(loggerMock.Object);
        var servicoRemovidoId = Guid.NewGuid();
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servicoRemovidoId);

        var resultado = await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 31m));

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.False(referencia.EstoqueDebitado);
        Assert.Empty(_estoqueUseCasesMock.Invocations);
        var (_, propriedades, _) = ObterRegistroDeLog(loggerMock, OrdemServicoLogEventos.OsFalha);
        Assert.Equal("estoque_debito", propriedades["Etapa"]);
        Assert.Equal("nao_encontrado", propriedades["TipoFalha"]);
        Assert.Equal($"Servico com ID '{servicoRemovidoId}' nao encontrado.", propriedades["Motivo"]);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task ProcessarPagamentoAprovadoAsync_DevePropagarExcecao_QuandoFalhaNaoForDeRegraDeNegocio()
    {
        var useCases = CriarUseCases();
        var servico = CriarServico("Troca de oleo", (2, 15.50m));
        var referencia = CriarReferenciaExistente(Guid.NewGuid(), Guid.NewGuid(), servico.Id);
        _estoqueUseCasesMock
            .Setup(e => e.DebitarEstoqueParaOrdemServicoAsync(It.IsAny<IReadOnlyCollection<Servico>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Banco indisponivel."));

        var action = async () => await useCases.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(referencia.Id, Guid.NewGuid(), 31m));

        await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.False(referencia.EstoqueDebitado);
        _referenciaGatewayMock.Verify(g => g.AtualizarAsync(It.IsAny<OrdemServicoReferencia>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }
}
