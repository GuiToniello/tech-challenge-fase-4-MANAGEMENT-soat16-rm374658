using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.OrdensServico;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using Xunit;

namespace TechChallenge.Oficina.Entities.Tests.Features.OrdensServico;

public sealed class OrdemServicoReferenciaDomainTests
{
    private static OrdemServicoReferencia CriarReferencia(params Guid[] servicoIds)
        => OrdemServicoReferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), servicoIds.Length == 0 ? [Guid.NewGuid()] : servicoIds);

    [Fact]
    public void OrdemServicoReferencia_Criar_DeveIniciarComStatusRecebida()
    {
        var ordemServicoId = Guid.NewGuid();
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();
        var servicoId = Guid.NewGuid();

        var referencia = OrdemServicoReferencia.Criar(ordemServicoId, clienteId, veiculoId, [servicoId]);

        Assert.Equal(ordemServicoId, referencia.Id);
        Assert.Equal(clienteId, referencia.ClienteId);
        Assert.Equal(veiculoId, referencia.VeiculoId);
        Assert.Equal(StatusOrdemServico.Recebida, referencia.Status);
        Assert.Equal([servicoId], referencia.ServicoIds);
    }

    [Fact]
    public void OrdemServicoReferencia_Criar_DeveRemoverServicosDuplicados()
    {
        var servicoId = Guid.NewGuid();

        var referencia = CriarReferencia(servicoId, servicoId);

        Assert.Single(referencia.ServicoIds);
    }

    [Fact]
    public void OrdemServicoReferencia_Criar_DeveLancarQuandoOrdemServicoIdForInvalido()
    {
        var action = () => OrdemServicoReferencia.Criar(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O identificador da ordem de servico e obrigatorio.", exception.Message);
    }

    [Fact]
    public void OrdemServicoReferencia_Atualizar_DeveLancarQuandoClienteForInvalido()
    {
        var referencia = CriarReferencia();

        var action = () => referencia.Atualizar(Guid.Empty, Guid.NewGuid(), [Guid.NewGuid()]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O cliente da ordem de servico e obrigatorio.", exception.Message);
    }

    [Fact]
    public void OrdemServicoReferencia_Atualizar_DeveLancarQuandoVeiculoForInvalido()
    {
        var referencia = CriarReferencia();

        var action = () => referencia.Atualizar(Guid.NewGuid(), Guid.Empty, [Guid.NewGuid()]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O veiculo da ordem de servico e obrigatorio.", exception.Message);
    }

    [Fact]
    public void OrdemServicoReferencia_Atualizar_DeveLancarQuandoNaoPossuirServicos()
    {
        var referencia = CriarReferencia();

        var action = () => referencia.Atualizar(Guid.NewGuid(), Guid.NewGuid(), []);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("A ordem de servico deve possuir ao menos um servico.", exception.Message);
    }

    [Fact]
    public void OrdemServicoReferencia_Atualizar_DeveLancarQuandoServicoPossuirIdentificadorInvalido()
    {
        var referencia = CriarReferencia();

        var action = () => referencia.Atualizar(Guid.NewGuid(), Guid.NewGuid(), [Guid.Empty]);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("Todos os servicos informados devem possuir identificador valido.", exception.Message);
    }

    [Fact]
    public void OrdemServicoReferencia_Atualizar_DeveSubstituirDadosSemAlterarStatus()
    {
        var referencia = CriarReferencia();
        referencia.AtualizarStatus(StatusOrdemServico.EmDiagnostico);
        var clienteId = Guid.NewGuid();
        var veiculoId = Guid.NewGuid();
        var servicoId = Guid.NewGuid();

        referencia.Atualizar(clienteId, veiculoId, [servicoId]);

        Assert.Equal(clienteId, referencia.ClienteId);
        Assert.Equal(veiculoId, referencia.VeiculoId);
        Assert.Equal([servicoId], referencia.ServicoIds);
        Assert.Equal(StatusOrdemServico.EmDiagnostico, referencia.Status);
    }

    [Fact]
    public void OrdemServicoReferencia_AtualizarStatus_DeveLancarQuandoStatusForInvalido()
    {
        var referencia = CriarReferencia();

        var action = () => referencia.AtualizarStatus((StatusOrdemServico)99);

        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal("O status da ordem de servico informado e invalido.", exception.Message);
        Assert.Equal(StatusOrdemServico.Recebida, referencia.Status);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Recebida)]
    [InlineData(StatusOrdemServico.EmDiagnostico)]
    [InlineData(StatusOrdemServico.AguardandoAprovacao)]
    public void OrdemServicoReferencia_ValidacoesDeOrcamento_DevemPermitirStatusAnterioresAExecucao(StatusOrdemServico status)
    {
        var referencia = CriarReferencia();
        referencia.AtualizarStatus(status);

        referencia.ValidarGeracaoOrcamento();
        referencia.ValidarAprovacaoOrcamento();
        referencia.ValidarRecusaOrcamento();
    }

    [Theory]
    [InlineData(StatusOrdemServico.EmExecucao)]
    [InlineData(StatusOrdemServico.Finalizada)]
    [InlineData(StatusOrdemServico.Entregue)]
    [InlineData(StatusOrdemServico.Encerrada)]
    [InlineData(StatusOrdemServico.Cancelada)]
    public void OrdemServicoReferencia_ValidarGeracaoOrcamento_DeveLancar_QuandoStatusNaoPermitir(StatusOrdemServico status)
    {
        var referencia = CriarReferencia();
        referencia.AtualizarStatus(status);

        var exception = Assert.Throws<DomainException>(referencia.ValidarGeracaoOrcamento);

        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento gerado.", exception.Message);
    }

    [Fact]
    public void OrdemServicoReferencia_ValidarAprovacaoERecusa_DevemLancar_QuandoOrdemFinalizada()
    {
        var referencia = CriarReferencia();
        referencia.AtualizarStatus(StatusOrdemServico.Finalizada);

        var aprovacao = Assert.Throws<DomainException>(referencia.ValidarAprovacaoOrcamento);
        var recusa = Assert.Throws<DomainException>(referencia.ValidarRecusaOrcamento);

        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento aprovado.", aprovacao.Message);
        Assert.Equal("Somente ordem de servico recebida, em diagnostico ou aguardando aprovacao pode ter orcamento recusado.", recusa.Message);
    }

    [Theory]
    [InlineData(StatusOrdemServico.Recebida, "Recebida")]
    [InlineData(StatusOrdemServico.EmDiagnostico, "Em diagnóstico")]
    [InlineData(StatusOrdemServico.AguardandoAprovacao, "Aguardando aprovação")]
    [InlineData(StatusOrdemServico.EmExecucao, "Em execução")]
    [InlineData(StatusOrdemServico.Finalizada, "Finalizada")]
    [InlineData(StatusOrdemServico.Entregue, "Entregue")]
    [InlineData(StatusOrdemServico.Encerrada, "Encerrada")]
    [InlineData(StatusOrdemServico.Cancelada, "Cancelada")]
    public void StatusOrdemServicoExtensions_ObterDescricao_DeveRetornarDescricaoEsperada(StatusOrdemServico status, string descricaoEsperada)
    {
        var descricao = status.ObterDescricao();

        Assert.Equal(descricaoEsperada, descricao);
    }

    [Fact]
    public void StatusOrdemServico_DeveManterValoresDoContratoDeEventos()
    {
        Assert.Equal(1, (int)StatusOrdemServico.Recebida);
        Assert.Equal(2, (int)StatusOrdemServico.EmDiagnostico);
        Assert.Equal(3, (int)StatusOrdemServico.AguardandoAprovacao);
        Assert.Equal(4, (int)StatusOrdemServico.EmExecucao);
        Assert.Equal(5, (int)StatusOrdemServico.Finalizada);
        Assert.Equal(6, (int)StatusOrdemServico.Entregue);
        Assert.Equal(7, (int)StatusOrdemServico.Encerrada);
        Assert.Equal(8, (int)StatusOrdemServico.Cancelada);
    }
}
