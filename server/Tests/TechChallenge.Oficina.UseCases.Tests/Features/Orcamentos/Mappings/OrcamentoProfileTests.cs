using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Mappings;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;
using Xunit;

namespace TechChallenge.Oficina.UseCases.Tests.Features.Orcamentos.Mappings;

public sealed class OrcamentoProfileTests
{
    private readonly IMapper _mapper;

    public OrcamentoProfileTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddAutoMapper(_ => { }, typeof(OrcamentoProfile).Assembly);

        using var serviceProvider = services.BuildServiceProvider();
        _mapper = serviceProvider.GetRequiredService<IMapper>();
    }

    [Fact]
    public void Map_OrcamentoParaOrcamentoViewModel_DeveMapearTotaisStatusEServicos()
    {
        var insumo = Insumo.Criar("Oleo", "Fabricante", 10, 19.90m);
        var servico = Servico.Criar("Troca de oleo", "Servico completo", [ItemServico.Criar(insumo, 2)]);
        var ordemServicoId = Guid.NewGuid();
        var dataGeracao = new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc);
        var orcamento = Orcamento.Criar(ordemServicoId, [servico], dataGeracao);

        var viewModel = _mapper.Map<OrcamentoViewModel>(orcamento);

        Assert.Equal(ordemServicoId, viewModel.OrdemServicoId);
        Assert.Equal(dataGeracao, viewModel.DataGeracao);
        Assert.Equal(39.80m, viewModel.ValorTotal);
        Assert.Equal(1, viewModel.Status);
        Assert.Equal("Gerado", viewModel.StatusDescricao);
        var servicoViewModel = Assert.Single(viewModel.Servicos);
        Assert.Equal(servico.Id, servicoViewModel.ServicoId);
        Assert.Equal("Troca de oleo", servicoViewModel.NomeServico);
        Assert.Equal(39.80m, servicoViewModel.ValorTotal);
    }

    [Fact]
    public void Map_OrcamentoAprovado_DeveMapearStatusAprovado()
    {
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [Servico.Criar("Alinhamento", "Servico completo", [])]);
        orcamento.Aprovar();

        var viewModel = _mapper.Map<OrcamentoViewModel>(orcamento);

        Assert.Equal(2, viewModel.Status);
        Assert.Equal("Aprovado", viewModel.StatusDescricao);
    }
}
