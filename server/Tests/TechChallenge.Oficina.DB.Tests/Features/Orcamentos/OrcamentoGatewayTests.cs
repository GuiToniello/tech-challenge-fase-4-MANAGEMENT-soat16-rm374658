using Microsoft.EntityFrameworkCore;
using TechChallenge.Oficina.DB.Data.Context;
using TechChallenge.Oficina.DB.Data.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Entities.Features.Servicos;
using Xunit;

namespace TechChallenge.Oficina.DB.Data.Tests.Features.Orcamentos;

public sealed class OrcamentoGatewayTests
{
    [Fact]
    public async Task AdicionarAsync_DevePersistirOrcamentoComServicos()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var servico = CriarServico("Troca de oleo", 25m, 2);
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico], new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));

        await using (var context = CriarContexto(nomeBanco))
        {
            await new OrcamentoGateway(context).AdicionarAsync(orcamento);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new OrcamentoGateway(novoContexto).ObterPorIdAsync(orcamento.OrdemServicoId);

        Assert.NotNull(salvo);
        Assert.Equal(50m, salvo!.ValorTotal);
        Assert.Equal(StatusOrcamento.Gerado, salvo.Status);
        var item = Assert.Single(salvo.Servicos);
        Assert.Equal(servico.Id, item.ServicoId);
        Assert.Equal("Troca de oleo", item.NomeServico);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirSomenteUltimoResultado_QuandoOrcamentoRegerado()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var servico = CriarServico("Troca", 25m, 2);
        var outroServico = CriarServico("Alinhamento", 30m, 1);
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico], new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrcamentoGateway(context);
            await gateway.AdicionarAsync(orcamento);

            var carregado = await gateway.ObterPorIdAsync(orcamento.OrdemServicoId);
            carregado!.Regerar([servico, outroServico], new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc));
            await gateway.AtualizarAsync(carregado);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new OrcamentoGateway(novoContexto).ObterPorIdAsync(orcamento.OrdemServicoId);

        Assert.Equal(80m, salvo!.ValorTotal);
        Assert.Equal(2, salvo.Servicos.Count);
        Assert.Equal(new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc), salvo.DataGeracao);
    }

    [Fact]
    public async Task AtualizarAsync_DevePersistirStatusAprovado()
    {
        var nomeBanco = Guid.NewGuid().ToString();
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", 25m, 2)]);

        await using (var context = CriarContexto(nomeBanco))
        {
            var gateway = new OrcamentoGateway(context);
            await gateway.AdicionarAsync(orcamento);
            orcamento.Aprovar();
            await gateway.AtualizarAsync(orcamento);
        }

        await using var novoContexto = CriarContexto(nomeBanco);
        var salvo = await new OrcamentoGateway(novoContexto).ObterPorIdAsync(orcamento.OrdemServicoId);

        Assert.Equal(StatusOrcamento.Aprovado, salvo!.Status);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveRetornarNull_QuandoNaoExiste()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());

        var encontrado = await new OrcamentoGateway(context).ObterPorIdAsync(Guid.NewGuid());

        Assert.Null(encontrado);
    }

    [Fact]
    public async Task RemoverAsync_DeveExcluirOrcamento()
    {
        await using var context = CriarContexto(Guid.NewGuid().ToString());
        var gateway = new OrcamentoGateway(context);
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServico("Troca", 25m, 2)]);
        await gateway.AdicionarAsync(orcamento);

        await gateway.RemoverAsync(orcamento);

        Assert.Empty(await context.Orcamentos.ToListAsync());
    }

    private static Servico CriarServico(string nome, decimal valorUnitario, int quantidade)
    {
        var insumo = Insumo.Criar($"Insumo {nome}", "Fabricante", 20, valorUnitario);
        return Servico.Criar(nome, "Servico completo", [ItemServico.Criar(insumo, quantidade)]);
    }

    private static OficinaDbContext CriarContexto(string nomeBanco)
    {
        var options = new DbContextOptionsBuilder<OficinaDbContext>()
            .UseInMemoryDatabase(nomeBanco)
            .Options;

        return new OficinaDbContext(options);
    }
}
