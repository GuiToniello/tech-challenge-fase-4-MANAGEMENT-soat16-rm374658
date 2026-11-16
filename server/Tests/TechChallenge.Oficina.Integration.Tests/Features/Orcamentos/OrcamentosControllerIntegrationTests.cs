using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Moq;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.Integration.Tests.Features.Orcamentos;

public sealed class OrcamentosControllerIntegrationTests : IDisposable
{
    private readonly IntegrationTestFixture _fixture;

    public OrcamentosControllerIntegrationTests()
    {
        _fixture = new IntegrationTestFixture();
    }

    public void Dispose() => _fixture.Dispose();

    private async Task<(Guid OrdemServicoId, Guid ClienteId, Guid InsumoId)> CriarOrdemServicoAsync(int quantidadeDisponivel = 20)
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture, quantidadeDisponivel);
        var ordemServicoId = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);
        return (ordemServicoId, clienteId, insumoId);
    }

    private async Task<int> ObterEstoqueAsync(Guid insumoId)
        => (await _fixture.ObterContexto().Insumos.SingleAsync(insumo => insumo.Id == insumoId)).QuantidadeDisponivel;

    [Fact]
    public async Task Gerar_OrdemRecebida_DeveRetornar200ComOrcamentoEPublicarEvento()
    {
        var (ordemServicoId, _, _) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.Gerar(ordemServicoId, CancellationToken.None));

        Assert.Equal(ordemServicoId, ok.Value!.OrdemServicoId);
        Assert.Equal(60m, ok.Value.ValorTotal);
        Assert.Equal(1, ok.Value.Status);
        Assert.Single(ok.Value.Servicos);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoGerado(ordemServicoId, 60m), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Gerar_DuasVezes_DeveManterUmUnicoOrcamento()
    {
        var (ordemServicoId, _, _) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        await endpoints.Gerar(ordemServicoId, CancellationToken.None);
        Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.Gerar(ordemServicoId, CancellationToken.None));

        Assert.Single(await _fixture.ObterContexto().Set<Orcamento>().ToListAsync());
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrcamentoGerado>(), ordemServicoId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Gerar_OrdemInexistente_DeveRetornar404()
    {
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await endpoints.Gerar(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Gerar_EstoqueInsuficiente_DeveRetornar400()
    {
        var (ordemServicoId, _, _) = await CriarOrdemServicoAsync(quantidadeDisponivel: 2);
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await endpoints.Gerar(ordemServicoId, CancellationToken.None));
        Assert.IsType<NotFound<Dictionary<string, string?>>>(await endpoints.GetById(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task Gerar_OrdemEmExecucao_DeveRetornar400()
    {
        var (ordemServicoId, clienteId, _) = await CriarOrdemServicoAsync();
        await CenarioOrdemServico.AlterarStatusAsync(_fixture, ordemServicoId, clienteId, (int)StatusOrdemServico.Recebida, (int)StatusOrdemServico.EmExecucao);
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await endpoints.Gerar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task GetById_OrcamentoGerado_DeveRetornar200()
    {
        var (ordemServicoId, _, _) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();
        await endpoints.Gerar(ordemServicoId, CancellationToken.None);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.GetById(ordemServicoId, CancellationToken.None));

        Assert.Equal("Gerado", ok.Value!.StatusDescricao);
    }

    [Fact]
    public async Task GetById_OrcamentoInexistente_DeveRetornar404()
    {
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await endpoints.GetById(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Enviar_OrcamentoGerado_DeveRetornar204EEnviarEmailAoCliente()
    {
        var (ordemServicoId, _, _) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();
        await endpoints.Gerar(ordemServicoId, CancellationToken.None);

        Assert.IsType<NoContent>(await endpoints.Enviar(ordemServicoId, CancellationToken.None));

        _fixture.OrcamentoEmailSenderMock.Verify(s => s.EnviarOrcamentoAsync(It.Is<Orcamento>(o => o.OrdemServicoId == ordemServicoId), "cliente@email.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Enviar_OrcamentoInexistente_DeveRetornar404()
    {
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await endpoints.Enviar(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Aprovar_OrcamentoGerado_DeveRetornar200DebitarEstoqueEPublicarEvento()
    {
        var (ordemServicoId, _, insumoId) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();
        await endpoints.Gerar(ordemServicoId, CancellationToken.None);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.Aprovar(ordemServicoId, CancellationToken.None));

        Assert.Equal(2, ok.Value!.Status);
        Assert.Equal(16, await ObterEstoqueAsync(insumoId));
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoAprovado(ordemServicoId), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aprovar_DuasVezes_DeveRetornar400SemDebitarNovamente()
    {
        var (ordemServicoId, _, insumoId) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();
        await endpoints.Gerar(ordemServicoId, CancellationToken.None);
        await endpoints.Aprovar(ordemServicoId, CancellationToken.None);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await endpoints.Aprovar(ordemServicoId, CancellationToken.None));

        Assert.Equal(16, await ObterEstoqueAsync(insumoId));
    }

    [Fact]
    public async Task Aprovar_EstoqueConsumidoPorOutraOrdem_DeveRetornar400()
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture, quantidadeDisponivel: 6);
        var ordemA = Guid.NewGuid();
        var ordemB = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemA, clienteId, veiculoId, servicoId);
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemB, clienteId, veiculoId, servicoId);
        var endpoints = _fixture.CriarOrcamentosEndpoints();
        Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.Gerar(ordemA, CancellationToken.None));
        Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.Gerar(ordemB, CancellationToken.None));
        Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.Aprovar(ordemA, CancellationToken.None));

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await endpoints.Aprovar(ordemB, CancellationToken.None));

        Assert.Equal(2, await ObterEstoqueAsync(insumoId));
    }

    [Fact]
    public async Task Recusar_OrcamentoGerado_DeveRetornar200ComStatusRecusadoEPublicarEvento()
    {
        var (ordemServicoId, _, insumoId) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();
        await endpoints.Gerar(ordemServicoId, CancellationToken.None);

        var ok = Assert.IsType<Ok<OrcamentoViewModel>>(await endpoints.Recusar(ordemServicoId, CancellationToken.None));

        Assert.Equal(3, ok.Value!.Status);
        Assert.Equal(20, await ObterEstoqueAsync(insumoId));
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(new OrcamentoRecusado(ordemServicoId), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Recusar_SemOrcamento_DeveRetornar404()
    {
        var (ordemServicoId, _, _) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();

        Assert.IsType<NotFound<Dictionary<string, string?>>>(await endpoints.Recusar(ordemServicoId, CancellationToken.None));
    }

    [Fact]
    public async Task Gerar_AposAprovacao_DeveRetornar400()
    {
        var (ordemServicoId, _, _) = await CriarOrdemServicoAsync();
        var endpoints = _fixture.CriarOrcamentosEndpoints();
        await endpoints.Gerar(ordemServicoId, CancellationToken.None);
        await endpoints.Aprovar(ordemServicoId, CancellationToken.None);

        Assert.IsType<BadRequest<Dictionary<string, string?>>>(await endpoints.Gerar(ordemServicoId, CancellationToken.None));
    }
}
