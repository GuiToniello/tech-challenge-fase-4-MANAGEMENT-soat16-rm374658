using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.Mappings;
using TechChallenge.Oficina.UseCases.Features.Insumos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.UseCases.Tests.Features.Insumos;

public sealed class InsumoProfileTests
{
    private readonly IMapper _mapper;

    public InsumoProfileTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(InsumoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
    }

    [Fact]
    public void Map_InsumoParaInsumoViewModel_DeveCopiarOsCampos()
    {
        var insumo = Insumo.Criar("Vela de Ignicao", "NGK", 20, 15.5m);

        var viewModel = _mapper.Map<InsumoViewModel>(insumo);

        Assert.Equal(insumo.Id, viewModel.Id);
        Assert.Equal("Vela de Ignicao", viewModel.Nome);
        Assert.Equal("NGK", viewModel.Fabricante);
        Assert.Equal(20, viewModel.QuantidadeDisponivel);
        Assert.Equal(15.5m, viewModel.ValorUnitario);
    }

    [Fact]
    public void Map_MovimentacoesParaViewModel_DeveCopiarOsCamposComAOrdemDeServicoSoNoDebito()
    {
        // Mesmo mapeamento de colecao usado na consulta do historico de movimentacoes de estoque
        var ocorridoEm = new DateTime(2026, 11, 27, 10, 15, 30, 123, DateTimeKind.Utc);
        var ordemServicoId = Guid.NewGuid();
        var cadastro = new MovimentacaoEstoque
        {
            InsumoId = Guid.NewGuid(),
            OcorridoEm = ocorridoEm,
            Tipo = TiposMovimentacaoEstoque.Cadastro,
            Origem = OrigensMovimentacaoEstoque.Acao,
            Quantidade = 20,
            SaldoAnterior = 0,
            SaldoNovo = 20
        };
        var debito = cadastro with
        {
            OcorridoEm = ocorridoEm.AddSeconds(1),
            Tipo = TiposMovimentacaoEstoque.DebitoOrdemServico,
            Origem = OrigensMovimentacaoEstoque.Evento,
            Quantidade = -4,
            SaldoAnterior = 20,
            SaldoNovo = 16,
            OrdemServicoId = ordemServicoId
        };

        var viewModels = _mapper.Map<IReadOnlyCollection<MovimentacaoEstoqueViewModel>>(new[] { cadastro, debito }).ToArray();

        Assert.Equal(2, viewModels.Length);
        Assert.Equal(ocorridoEm, viewModels[0].OcorridoEm);
        Assert.Equal("Cadastro", viewModels[0].Tipo);
        Assert.Equal("acao", viewModels[0].Origem);
        Assert.Equal(20, viewModels[0].Quantidade);
        Assert.Equal(0, viewModels[0].SaldoAnterior);
        Assert.Equal(20, viewModels[0].SaldoNovo);
        Assert.Null(viewModels[0].OrdemServicoId);
        Assert.Equal("DebitoOrdemServico", viewModels[1].Tipo);
        Assert.Equal("evento", viewModels[1].Origem);
        Assert.Equal(-4, viewModels[1].Quantidade);
        Assert.Equal(20, viewModels[1].SaldoAnterior);
        Assert.Equal(16, viewModels[1].SaldoNovo);
        Assert.Equal(ordemServicoId, viewModels[1].OrdemServicoId);
    }
}
