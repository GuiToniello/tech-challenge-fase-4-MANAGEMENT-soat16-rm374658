using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Insumos;
using TechChallenge.Oficina.Entities.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Entities.Features.Servicos;
using Xunit;

namespace TechChallenge.Oficina.Entities.Tests.Features.Orcamentos;

public sealed class OrcamentoDomainTests
{
    private static Servico CriarServicoComItens(string nome, params (int quantidade, decimal valorUnitario)[] itens)
    {
        var itensServico = itens
            .Select((item, indice) =>
            {
                var insumo = Insumo.Criar($"Insumo {indice + 1}", "Fabricante", 10, item.valorUnitario);
                return ItemServico.Criar(insumo, item.quantidade);
            })
            .ToArray();

        return Servico.Criar(nome, "Servico completo", itensServico);
    }

    [Fact]
    public void Orcamento_Criar_DeveCalcularValoresTotaisEIniciarComoGerado()
    {
        var ordemServicoId = Guid.NewGuid();
        var servicoA = CriarServicoComItens("Troca de oleo", (2, 15.50m), (1, 10m));
        var servicoB = CriarServicoComItens("Alinhamento", (1, 30m));
        var dataGeracao = new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc);

        var orcamento = Orcamento.Criar(ordemServicoId, [servicoA, servicoB], dataGeracao);

        Assert.Equal(ordemServicoId, orcamento.Id);
        Assert.Equal(ordemServicoId, orcamento.OrdemServicoId);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
        Assert.Equal(dataGeracao, orcamento.DataGeracao);
        Assert.Equal(71m, orcamento.ValorTotal);
        Assert.Equal(41m, orcamento.Servicos.Single(s => s.ServicoId == servicoA.Id).ValorTotal);
        Assert.Equal(30m, orcamento.Servicos.Single(s => s.ServicoId == servicoB.Id).ValorTotal);
    }

    [Fact]
    public void Orcamento_Criar_DeveIgnorarServicosDuplicados()
    {
        var servico = CriarServicoComItens("Troca", (1, 50m));

        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico, servico]);

        Assert.Single(orcamento.Servicos);
        Assert.Equal(50m, orcamento.ValorTotal);
    }

    [Fact]
    public void Orcamento_Criar_DeveLancarQuandoOrdemServicoIdForInvalido()
    {
        var servico = CriarServicoComItens("Troca", (1, 10m));

        var action = () => Orcamento.Criar(Guid.Empty, [servico]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A ordem de servico do orcamento e obrigatoria.", exception.Message);
    }

    [Fact]
    public void Orcamento_Criar_DeveLancarQuandoDataGeracaoForInvalida()
    {
        var servico = CriarServicoComItens("Troca", (1, 10m));

        var action = () => Orcamento.Criar(Guid.NewGuid(), [servico], default(DateTime));

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A data de geracao do orcamento e obrigatoria.", exception.Message);
    }

    [Fact]
    public void Orcamento_Criar_DeveLancarQuandoNaoPossuirServicos()
    {
        var action = () => Orcamento.Criar(Guid.NewGuid(), []);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A ordem de servico deve possuir ao menos um servico para gerar orcamento.", exception.Message);
    }

    [Fact]
    public void OrcamentoServico_Criar_DeveLancarQuandoServicoForNulo()
    {
        var action = () => OrcamentoServico.Criar(null!);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O servico do orcamento e obrigatorio.", exception.Message);
    }

    [Fact]
    public void Orcamento_Regerar_DeveManterSomenteUltimoResultado()
    {
        var servico = CriarServicoComItens("Troca", (1, 50m));
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico], new DateTime(2024, 01, 01, 9, 00, 00, DateTimeKind.Utc));
        servico.ItensServico.Single().Insumo.AtualizarValorUnitario(80m);
        var novaData = new DateTime(2024, 01, 01, 10, 00, 00, DateTimeKind.Utc);

        orcamento.Regerar([servico], novaData);

        Assert.Equal(80m, orcamento.ValorTotal);
        Assert.Equal(novaData, orcamento.DataGeracao);
        Assert.Single(orcamento.Servicos);
        Assert.Equal(StatusOrcamento.Gerado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Regerar_DeveLancarSemAlterarOrcamento_QuandoJaFoiAprovado()
    {
        var servico = CriarServicoComItens("Troca", (1, 50m));
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico]);
        orcamento.Aprovar();

        var action = () => orcamento.Regerar([CriarServicoComItens("Alinhamento", (1, 30m))]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser gerado novamente.", exception.Message);
        Assert.Equal(50m, orcamento.ValorTotal);
        Assert.Equal(servico.Id, orcamento.Servicos.Single().ServicoId);
    }

    [Fact]
    public void Orcamento_Regerar_DeveLancarSemAlterarOrcamento_QuandoNaoPossuirServicos()
    {
        var servico = CriarServicoComItens("Troca", (1, 50m));
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [servico]);

        var action = () => orcamento.Regerar([]);

        Assert.Throws<DomainException>(action);
        Assert.Single(orcamento.Servicos);
        Assert.Equal(50m, orcamento.ValorTotal);
    }

    [Fact]
    public void Orcamento_Aprovar_DeveAlterarStatusParaAprovado()
    {
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServicoComItens("Troca", (1, 50m))]);

        orcamento.Aprovar();

        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Aprovar_DeveLancar_QuandoJaFoiAprovado()
    {
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServicoComItens("Troca", (1, 50m))]);
        orcamento.Aprovar();

        var action = () => orcamento.Aprovar();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser aprovado.", exception.Message);
    }

    [Fact]
    public void Orcamento_Aprovar_DeveLancar_QuandoJaFoiRecusado()
    {
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServicoComItens("Troca", (1, 50m))]);
        orcamento.Recusar();

        var action = () => orcamento.ValidarAprovacao();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser aprovado.", exception.Message);
        Assert.Equal(StatusOrcamento.Recusado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Recusar_DeveAlterarStatusParaRecusado()
    {
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServicoComItens("Troca", (1, 50m))]);

        orcamento.Recusar();

        Assert.Equal(StatusOrcamento.Recusado, orcamento.Status);
    }

    [Fact]
    public void Orcamento_Recusar_DeveLancar_QuandoJaFoiAprovado()
    {
        var orcamento = Orcamento.Criar(Guid.NewGuid(), [CriarServicoComItens("Troca", (1, 50m))]);
        orcamento.Aprovar();

        var action = () => orcamento.Recusar();

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Somente orcamento gerado pode ser recusado.", exception.Message);
        Assert.Equal(StatusOrcamento.Aprovado, orcamento.Status);
    }

    [Theory]
    [InlineData(StatusOrcamento.Gerado, "Gerado")]
    [InlineData(StatusOrcamento.Aprovado, "Aprovado")]
    [InlineData(StatusOrcamento.Recusado, "Recusado")]
    public void StatusOrcamentoExtensions_ObterDescricao_DeveRetornarDescricaoEsperada(StatusOrcamento status, string descricaoEsperada)
    {
        Assert.Equal(descricaoEsperada, status.ObterDescricao());
    }

    [Fact]
    public void StatusOrcamentoExtensions_ObterDescricao_DeveLancar_QuandoStatusInvalido()
    {
        var action = () => ((StatusOrcamento)99).ObterDescricao();

        Assert.Throws<ArgumentOutOfRangeException>(action);
    }
}
