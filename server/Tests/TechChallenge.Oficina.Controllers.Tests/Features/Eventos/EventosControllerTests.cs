using System.Text.Json;
using Moq;
using TechChallenge.Oficina.Controllers.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Commands;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Controllers.Tests.Features.Eventos;

public sealed class EventosControllerTests
{
    private readonly Mock<IOrdemServicoReferenciaUseCases> _useCasesMock = new();

    private EventosController CriarController() => new(_useCasesMock.Object);

    [Theory]
    [InlineData("OrdemServicoAberta")]
    [InlineData("OrdemServicoAtualizada")]
    public async Task ProcessarAsync_DeveRegistrarReferencia_QuandoOrdemAbertaOuAtualizada(string tipo)
    {
        var controller = CriarController();
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();
        var servicoId = Guid.NewGuid();
        var dados = $$"""{"ordemServicoId":"{{ordemServicoId}}","clienteId":"{{clienteId}}","veiculoId":"{{veiculoId}}","servicoIds":["{{servicoId}}"]}""";
        _useCasesMock
            .Setup(u => u.RegistrarAsync(It.IsAny<RegistrarOrdemServicoReferenciaCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var resultado = await controller.ProcessarAsync(tipo, dados, CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _useCasesMock.Verify(u => u.RegistrarAsync(
            It.Is<RegistrarOrdemServicoReferenciaCommand>(c => c.OrdemServicoId == ordemServicoId && c.ClienteId == clienteId && c.VeiculoId == veiculoId && c.ServicoIds.Single() == servicoId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarAsync_DeveRegistrarComListaVazia_QuandoServicoIdsAusente()
    {
        var controller = CriarController();
        var dados = $$"""{"ordemServicoId":"{{Guid.NewGuid()}}","clienteId":"{{Guid.NewGuid()}}","veiculoId":"{{Guid.NewGuid()}}"}""";

        await controller.ProcessarAsync(nameof(OrdemServicoAberta), dados, CancellationToken.None);

        _useCasesMock.Verify(u => u.RegistrarAsync(It.Is<RegistrarOrdemServicoReferenciaCommand>(c => c.ServicoIds.Count == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarAsync_DeveAtualizarStatus_QuandoStatusAlterado()
    {
        var controller = CriarController();
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var dados = $$"""{"ordemServicoId":"{{ordemServicoId}}","clienteId":"{{clienteId}}","statusAnterior":1,"statusNovo":2}""";
        _useCasesMock
            .Setup(u => u.AtualizarStatusAsync(new OrdemServicoStatusAlterado(ordemServicoId, clienteId, 1, 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Ignorado);

        var resultado = await controller.ProcessarAsync(nameof(OrdemServicoStatusAlterado), dados, CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        _useCasesMock.Verify(u => u.AtualizarStatusAsync(new OrdemServicoStatusAlterado(ordemServicoId, clienteId, 1, 2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessarAsync_DeveRemoverReferencia_QuandoOrdemExcluida()
    {
        var controller = CriarController();
        var ordemServicoId = Guid.NewGuid();
        _useCasesMock
            .Setup(u => u.RemoverAsync(new OrdemServicoExcluida(ordemServicoId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var resultado = await controller.ProcessarAsync(nameof(OrdemServicoExcluida), $$"""{"ordemServicoId":"{{ordemServicoId}}"}""", CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
    }

    [Fact]
    public async Task ProcessarAsync_DeveProcessarPagamento_QuandoPagamentoAprovado()
    {
        var controller = CriarController();
        var ordemServicoId = Guid.NewGuid();
        var pagamentoId = Guid.NewGuid();
        var dados = $$"""{"ordemServicoId":"{{ordemServicoId}}","pagamentoId":"{{pagamentoId}}","valor":150.75}""";
        _useCasesMock
            .Setup(u => u.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(ordemServicoId, pagamentoId, 150.75m), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResultadoProcessamentoEvento.Processado);

        var resultado = await controller.ProcessarAsync(nameof(PagamentoAprovado), dados, CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        _useCasesMock.Verify(u => u.ProcessarPagamentoAprovadoAsync(new PagamentoAprovado(ordemServicoId, pagamentoId, 150.75m), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("OrcamentoGerado")]
    [InlineData("OrdemServicoRejeitada")]
    [InlineData("OrdemServicoValidada")]
    [InlineData("EventoDesconhecido")]
    public async Task ProcessarAsync_DeveIgnorarSemChamarCasosDeUso_QuandoTipoNaoTratado(string tipo)
    {
        var controller = CriarController();

        var resultado = await controller.ProcessarAsync(tipo, "{}", CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.Empty(_useCasesMock.Invocations);
    }

    [Fact]
    public async Task ProcessarAsync_DeveLancarJsonException_QuandoDadosInvalidos()
    {
        var controller = CriarController();

        var action = async () => await controller.ProcessarAsync(nameof(OrdemServicoAberta), "{nao e json", CancellationToken.None);

        await Assert.ThrowsAnyAsync<JsonException>(action);
        Assert.Empty(_useCasesMock.Invocations);
    }

    [Fact]
    public async Task ProcessarAsync_DeveLancarJsonException_QuandoDadosNulos()
    {
        var controller = CriarController();

        var action = async () => await controller.ProcessarAsync(nameof(OrdemServicoExcluida), "null", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<JsonException>(action);
        Assert.Equal("Os dados do evento OrdemServicoExcluida nao foram informados.", exception.Message);
    }

    [Fact]
    public async Task ProcessarAsync_DevePropagarExcecaoDoCasoDeUso()
    {
        var controller = CriarController();
        _useCasesMock
            .Setup(u => u.RemoverAsync(It.IsAny<OrdemServicoExcluida>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Banco indisponivel."));

        var action = async () => await controller.ProcessarAsync(nameof(OrdemServicoExcluida), $$"""{"ordemServicoId":"{{Guid.NewGuid()}}"}""", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(action);
    }
}
