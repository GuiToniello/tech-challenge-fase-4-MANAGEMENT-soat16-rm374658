using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TechChallenge.Oficina.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using Xunit;

namespace TechChallenge.Oficina.Controllers.Tests.Features.Orcamentos;

public sealed class OrcamentosControllerTests
{
    private readonly Mock<IOrcamentoUseCases> _useCasesMock = new();
    private readonly Mock<IOrcamentosAdapter> _adapterMock = new();

    private OrcamentosController CriarController(ILogger<OrcamentosController>? logger = null)
    {
        return new OrcamentosController(_useCasesMock.Object, _adapterMock.Object, logger ?? NullLogger<OrcamentosController>.Instance);
    }

    private static (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades) ObterRegistroDeFalha(Mock<ILogger<OrcamentosController>> loggerMock)
    {
        var invocacao = Assert.Single(loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.OsFalha.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades);
    }

    [Fact]
    public async Task GetById_DeveRepassarQueryERetornarAdaptado()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.ObterPorOrdemServicoIdAsync(It.Is<ObterOrcamentoPorOrdemServicoIdQuery>(q => q.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrcamentoViewModel { OrdemServicoId = id });
        _adapterMock
            .Setup(a => a.Adapt(It.Is<OrcamentosResult<OrcamentoViewModel, Exception>>(r => r.Value!.OrdemServicoId == id)))
            .Returns(adaptado);

        var resposta = await controller.GetById(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task GetById_DeveAdaptarErroSemRegistrarFalha_QuandoKeyNotFoundException()
    {
        var loggerMock = new Mock<ILogger<OrcamentosController>>();
        var controller = CriarController(loggerMock.Object);
        var excecao = new KeyNotFoundException("Orcamento nao encontrado.");
        _useCasesMock
            .Setup(u => u.ObterPorOrdemServicoIdAsync(It.IsAny<ObterOrcamentoPorOrdemServicoIdQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(excecao);

        await controller.GetById(Guid.NewGuid(), CancellationToken.None);

        _adapterMock.Verify(a => a.Adapt(It.Is<OrcamentosResult<OrcamentoViewModel, Exception>>(r => r.Error == excecao)), Times.Once);
        Assert.Empty(loggerMock.Invocations);
    }

    [Fact]
    public async Task Gerar_DeveRepassarComandoComId()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.GerarAsync(It.Is<GerarOrcamentoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrcamentoViewModel { OrdemServicoId = id });
        _adapterMock.Setup(a => a.Adapt(It.IsAny<OrcamentosResult<OrcamentoViewModel, Exception>>())).Returns(adaptado);

        var resposta = await controller.Gerar(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
        _useCasesMock.Verify(u => u.GerarAsync(It.Is<GerarOrcamentoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gerar_DeveRegistrarFalhaDeRegraDeNegocio_QuandoDomainException()
    {
        var loggerMock = new Mock<ILogger<OrcamentosController>>();
        var controller = CriarController(loggerMock.Object);
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.GerarAsync(It.IsAny<GerarOrcamentoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Estoque insuficiente."));

        await controller.Gerar(id, CancellationToken.None);

        var (nivel, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal("orcamento_geracao", propriedades["Etapa"]);
        Assert.Equal(id, propriedades["OrdemServicoId"]);
        Assert.Equal("regra_negocio", propriedades["TipoFalha"]);
        Assert.Equal("Estoque insuficiente.", propriedades["Motivo"]);
        _adapterMock.Verify(a => a.Adapt(It.Is<OrcamentosResult<OrcamentoViewModel, Exception>>(r => r.Error is DomainException)), Times.Once);
    }

    [Fact]
    public async Task Enviar_DeveRetornarVazio_QuandoSucesso()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var vazio = new object();
        _adapterMock.Setup(a => a.Empty()).Returns(vazio);

        var resposta = await controller.Enviar(id, CancellationToken.None);

        Assert.Same(vazio, resposta);
        _useCasesMock.Verify(u => u.EnviarPorEmailAsync(It.Is<EnviarOrcamentoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enviar_DeveRegistrarFalhaDeNaoEncontrado_QuandoKeyNotFoundException()
    {
        var loggerMock = new Mock<ILogger<OrcamentosController>>();
        var controller = CriarController(loggerMock.Object);
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.EnviarPorEmailAsync(It.IsAny<EnviarOrcamentoCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Orcamento nao encontrado."));

        await controller.Enviar(id, CancellationToken.None);

        var (_, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal("orcamento_envio", propriedades["Etapa"]);
        Assert.Equal("nao_encontrado", propriedades["TipoFalha"]);
        _adapterMock.Verify(a => a.Adapt(It.Is<OrcamentosResult<bool, Exception>>(r => r.Error is KeyNotFoundException)), Times.Once);
    }

    [Fact]
    public async Task Aprovar_DeveRegistrarFalhaDeRegraDeNegocio_QuandoDomainException()
    {
        var loggerMock = new Mock<ILogger<OrcamentosController>>();
        var controller = CriarController(loggerMock.Object);
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.AprovarAsync(It.Is<AprovarOrcamentoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Somente orcamento gerado pode ser aprovado."));

        await controller.Aprovar(id, CancellationToken.None);

        var (nivel, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Equal("orcamento_aprovacao", propriedades["Etapa"]);
        Assert.Equal(id, propriedades["OrdemServicoId"]);
        Assert.Equal("regra_negocio", propriedades["TipoFalha"]);
        Assert.Equal("Somente orcamento gerado pode ser aprovado.", propriedades["Motivo"]);
    }

    [Fact]
    public async Task Aprovar_DeveRetornarAdaptado_QuandoSucesso()
    {
        var controller = CriarController();
        var id = Guid.NewGuid();
        var adaptado = new object();
        _useCasesMock
            .Setup(u => u.AprovarAsync(It.Is<AprovarOrcamentoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrcamentoViewModel { OrdemServicoId = id, Status = 2 });
        _adapterMock.Setup(a => a.Adapt(It.IsAny<OrcamentosResult<OrcamentoViewModel, Exception>>())).Returns(adaptado);

        var resposta = await controller.Aprovar(id, CancellationToken.None);

        Assert.Same(adaptado, resposta);
    }

    [Fact]
    public async Task Recusar_DeveRegistrarFalhaDeNaoEncontrado_QuandoKeyNotFoundException()
    {
        var loggerMock = new Mock<ILogger<OrcamentosController>>();
        var controller = CriarController(loggerMock.Object);
        var id = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.RecusarAsync(It.Is<RecusarOrcamentoCommand>(c => c.OrdemServicoId == id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Ordem de servico nao encontrada."));

        await controller.Recusar(id, CancellationToken.None);

        var (_, propriedades) = ObterRegistroDeFalha(loggerMock);
        Assert.Equal("orcamento_recusa", propriedades["Etapa"]);
        Assert.Equal("nao_encontrado", propriedades["TipoFalha"]);
        Assert.Equal("Ordem de servico nao encontrada.", propriedades["Motivo"]);
    }
}
