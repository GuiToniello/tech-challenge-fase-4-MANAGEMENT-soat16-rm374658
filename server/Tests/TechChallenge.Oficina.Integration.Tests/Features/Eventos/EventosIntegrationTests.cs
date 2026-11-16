using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;
using Xunit;

namespace TechChallenge.Oficina.Integration.Tests.Features.Eventos;

public sealed class EventosIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture;

    public EventosIntegrationTests()
    {
        _fixture = new IntegrationTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task OrdemServicoAberta_DadosValidos_DeveRegistrarReferenciaRecebida()
    {
        var (clienteId, veiculoId, _, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();

        var resultado = await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        var referencia = await _fixture.ObterContexto().OrdensServicoReferencias.SingleAsync();
        Assert.Equal(ordemServicoId, referencia.Id);
        Assert.Equal(StatusOrdemServico.Recebida, referencia.Status);
        Assert.Equal([servicoId], referencia.ServicoIds);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoRejeitada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrdemServicoAberta_VeiculoDeOutroCliente_DevePublicarRejeicaoSemRegistrar()
    {
        var (clienteDonoId, veiculoId, _, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var outroClienteId = await CenarioOrdemServico.CriarClienteAsync(_fixture, "123.456.789-09", "outro@email.com");
        var ordemServicoId = Guid.NewGuid();

        var resultado = await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, outroClienteId, veiculoId, servicoId);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.NotEqual(clienteDonoId, outroClienteId);
        Assert.Empty(await _fixture.ObterContexto().OrdensServicoReferencias.ToListAsync());
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(
            new OrdemServicoRejeitada(ordemServicoId, "O veiculo informado deve estar vinculado ao cliente da ordem de servico."),
            ordemServicoId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrdemServicoAberta_ServicoInexistente_DevePublicarRejeicao()
    {
        var (clienteId, veiculoId, _, _) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();
        var servicoInexistenteId = Guid.NewGuid();

        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoInexistenteId);

        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(
            new OrdemServicoRejeitada(ordemServicoId, $"Servico com ID '{servicoInexistenteId}' nao encontrado."),
            ordemServicoId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrdemServicoStatusAlterado_DeveAtualizarStatusENotificarCliente()
    {
        var (clienteId, veiculoId, _, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);

        var resultado = await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, clienteId, (int)StatusOrdemServico.Recebida, (int)StatusOrdemServico.EmDiagnostico);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        var referencia = await _fixture.ObterContexto().OrdensServicoReferencias.SingleAsync();
        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
        _fixture.StatusEmailSenderMock.Verify(s => s.EnviarStatusAlteradoAsync(
            It.Is<NotificacaoStatusOrdemServico>(n => n.OrdemServicoId == ordemServicoId && n.StatusNovo == StatusOrdemServico.EmDiagnostico && n.Servicos.Single() == "Troca de Vela"),
            "cliente@email.com",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrdemServicoStatusAlterado_OrdemDesconhecida_DeveSerIgnorado()
    {
        var resultado = await CenarioOrdemServico.AlterarStatusAsync(_fixture, Guid.NewGuid(), Guid.NewGuid(), 1, 2);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
    }

    [Fact]
    public async Task OrdemServicoExcluida_DeveRemoverReferenciaEOrcamento()
    {
        var (clienteId, veiculoId, _, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);
        var orcamentos = _fixture.CriarOrcamentosEndpoints();
        Assert.IsType<Ok<OrcamentoViewModel>>(await orcamentos.Gerar(ordemServicoId, CancellationToken.None));

        var resultado = await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Empty(await _fixture.ObterContexto().OrdensServicoReferencias.ToListAsync());
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await orcamentos.GetById(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task EventoNaoTratado_DeveSerIgnorado()
    {
        var resultado = await _fixture.CriarEventosController().ProcessarAsync(nameof(OrcamentoGerado), "{}", CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
    }
}
