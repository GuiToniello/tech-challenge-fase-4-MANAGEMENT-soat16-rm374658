using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.DB.Data.Context;
using TechChallenge.Oficina.DB.Data.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using Xunit;

namespace TechChallenge.Oficina.DB.Data.Tests.Features.OrdensServico;

public sealed class OrdemServicoReferenciaGatewayTests
{
    [Fact]
    public async Task AdicionarAsync_DevePersistirReferenciaComServicos()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var servicoIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), servicoIds);

        await using (var context = CriarContexto(nomeBanco))
        {
            await new OrdemServicoReferenciaGateway(context).AdicionarAsync(referencia);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new OrdemServicoReferenciaGateway(novoContexto).ObterPorIdAsync(referencia.Id);

        Assert.NotNull(salva);
        Assert.Equal(referencia.ClienteId, salva!.ClienteId);
        Assert.Equal(referencia.VeiculoId, salva.VeiculoId);
        Assert.Equal(StatusOrdemServico.Recebida, salva.Status);
        Assert.Equal(servicoIds, salva.ServicoIds);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirNovosServicosEStatus()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]);
        var novoServicoId = Guid.NewGuid();

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrdemServicoReferenciaGateway(context);
            await gateway.AdicionarAsync(referencia);

            var carregada = await gateway.ObterPorIdAsync(referencia.Id);
            carregada!.Atualizar(carregada.ClienteId, carregada.VeiculoId, [novoServicoId]);
            carregada.AtualizarStatus(StatusOrdemServico.EmDiagnostico);
            await gateway.AtualizarAsync(carregada);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new OrdemServicoReferenciaGateway(novoContexto).ObterPorIdAsync(referencia.Id);

        Assert.Equal([novoServicoId], salva!.ServicoIds);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, salva.Status);
        Assert.False(salva.EstoqueDebitado);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirDebitoDeEstoque()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]);

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrdemServicoReferenciaGateway(context);
            await gateway.AdicionarAsync(referencia);

            var carregada = await gateway.ObterPorIdAsync(referencia.Id);
            carregada!.RegistrarDebitoEstoque();
            await gateway.AtualizarAsync(carregada);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salva = await new OrdemServicoReferenciaGateway(novoContexto).ObterPorIdAsync(referencia.Id);

        Assert.True(salva!.EstoqueDebitado);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarNull_QuandoNaoExiste()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());

        var encontrada = await new OrdemServicoReferenciaGateway(context).ObterPorIdAsync(Guid.NewGuid());

        Assert.Null(encontrada);
    }

    [Fact]
    public async Task RemoverAsync_DeveExcluirReferencia()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());
        var gateway = new OrdemServicoReferenciaGateway(context);
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]);
        await gateway.AdicionarAsync(referencia);

        await gateway.RemoverAsync(referencia);

        Assert.Empty(await context.OrdensServicoReferencias.ToListAsync());
    }

    [Fact]
    public async Task ExecutarEmTransacaoAsync_DeveExecutarOperacaoEGravar_QuandoProvedorNaoRelacional()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var referencia = OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]);

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrdemServicoReferenciaGateway(context);
            await gateway.ExecutarEmTransacaoAsync(() => gateway.AdicionarAsync(referencia));
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        Assert.NotNull(await new OrdemServicoReferenciaGateway(novoContexto).ObterPorIdAsync(referencia.Id));
    }

    [Fact]
    public async Task ExecutarEmTransacaoAsync_DevePropagarExcecaoDaOperacao()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());
        var gateway = new OrdemServicoReferenciaGateway(context);

        var action = () => gateway.ExecutarEmTransacaoAsync(() => throw new InvalidOperationException("Falha no meio da operacao."));

        await Assert.ThrowsAsync<InvalidOperationException>(action);
    }

    private static OficinaDbContext CriarContexto(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<OficinaDbContext>()
            .UseInMemoryDatabase(nomeBanco)
            .Options;

        return new OficinaDbContext(options);
    }
}
