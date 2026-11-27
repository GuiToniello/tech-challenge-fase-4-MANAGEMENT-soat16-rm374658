using Microsoft.EntityFrameworkCore;
using Moq;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;
using TechChallenge.Oficina.Integration.Tests.Infrastructure;
using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
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

    private async Task<int> ObterEstoqueAsync(Guid insumoId)
        => (await _fixture.ObterContexto().Insumos.SingleAsync(insumo => insumo.Id == insumoId)).QuantidadeDisponivel;

    private async Task<bool> ObterEstoqueDebitadoAsync(Guid ordemServicoId)
        => (await _fixture.ObterContexto().OrdensServicoReferencias.SingleAsync(referencia => referencia.Id == ordemServicoId)).EstoqueDebitado;

    private List<OrdemServicoValidada> CapturarValidacoesPublicadas()
    {
        var eventos = new List<OrdemServicoValidada>();

        _fixture.EventosPublisherMock
            .Setup(p => p.PublicarAsync(It.IsAny<OrdemServicoValidada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<OrdemServicoValidada, Guid, CancellationToken>((evento, _, _) => eventos.Add(evento))
            .Returns(Task.CompletedTask);

        return eventos;
    }

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
        Assert.False(referencia.EstoqueDebitado);
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoRejeitada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrdemServicoAberta_DadosValidos_DevePublicarOrdemServicoValidadaComServicosPrecificados()
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();
        var validacoes = CapturarValidacoesPublicadas();

        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);

        var evento = Assert.Single(validacoes);
        Assert.Equal(ordemServicoId, evento.OrdemServicoId);
        Assert.Equal(clienteId, evento.ClienteId);
        Assert.Equal(veiculoId, evento.VeiculoId);
        Assert.Equal("cliente@email.com", evento.ClienteEmail);
        Assert.True(evento.EstoqueSuficiente);
        Assert.Equal([new ServicoValidado(servicoId, "Troca de Vela", 60m)], evento.Servicos);
        Assert.Equal(60m, evento.ValorTotal);
        Assert.Equal(20, await ObterEstoqueAsync(insumoId));
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoValidada>(), ordemServicoId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrdemServicoAberta_EstoqueInsuficiente_DeveRegistrarEPublicarValidadaSemEstoque()
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture, quantidadeDisponivel: 2);
        var ordemServicoId = Guid.NewGuid();
        var validacoes = CapturarValidacoesPublicadas();

        var resultado = await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Single(await _fixture.ObterContexto().OrdensServicoReferencias.ToListAsync());
        Assert.False(Assert.Single(validacoes).EstoqueSuficiente);
        Assert.Equal(2, await ObterEstoqueAsync(insumoId));
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
        _fixture.EventosPublisherMock.Verify(p => p.PublicarAsync(It.IsAny<OrdemServicoValidada>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
    public async Task OrdemServicoExcluida_DeveRemoverReferencia()
    {
        var (clienteId, veiculoId, _, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);

        var resultado = await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, resultado);
        Assert.Empty(await _fixture.ObterContexto().OrdensServicoReferencias.ToListAsync());
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.ExcluirOrdemServicoAsync(_fixture, ordemServicoId));
    }

    [Fact]
    public async Task PagamentoAprovado_DeveDebitarEstoqueUmaUnicaVez()
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);

        var primeiroPagamento = await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemServicoId);
        var pagamentoRepetido = await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemServicoId);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, primeiroPagamento);
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, pagamentoRepetido);
        Assert.Equal(16, await ObterEstoqueAsync(insumoId));
        Assert.True(await ObterEstoqueDebitadoAsync(ordemServicoId));
    }

    [Fact]
    public async Task PagamentoAprovado_EstoqueConsumidoPorOutraOrdem_DeveSerIgnoradoSemDebito()
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture, quantidadeDisponivel: 6);
        var ordemA = Guid.NewGuid();
        var ordemB = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemA, clienteId, veiculoId, servicoId);
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemB, clienteId, veiculoId, servicoId);
        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemA));

        var resultado = await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemB);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
        Assert.Equal(2, await ObterEstoqueAsync(insumoId));
        Assert.True(await ObterEstoqueDebitadoAsync(ordemA));
        Assert.False(await ObterEstoqueDebitadoAsync(ordemB));
    }

    [Fact]
    public async Task PagamentoAprovado_OrdemDesconhecida_DeveSerIgnorado()
    {
        var resultado = await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, Guid.NewGuid());

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
    }

    [Fact]
    public async Task PagamentoAprovado_DeveRegistrarODebitoNoHistoricoDeMovimentacoesUmaUnicaVez()
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture);
        var ordemServicoId = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemServicoId, clienteId, veiculoId, servicoId);

        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemServicoId));
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemServicoId));

        // Cadastro do insumo (20 unidades) e o debito das 4 do servico; o pagamento repetido e ignorado e nao gera outro registro
        var movimentacoes = await CenarioOrdemServico.ObterMovimentacoesAsync(_fixture, insumoId);
        Assert.Equal(["Cadastro", "DebitoOrdemServico"], movimentacoes.Select(movimentacao => movimentacao.Tipo));
        var debito = movimentacoes[1];
        Assert.Equal("evento", debito.Origem);
        Assert.Equal(ordemServicoId, debito.OrdemServicoId);
        Assert.Equal((-4, 20, 16), (debito.Quantidade, debito.SaldoAnterior, debito.SaldoNovo));
        Assert.Equal(16, await ObterEstoqueAsync(insumoId));
    }

    [Fact]
    public async Task PagamentoAprovado_SemDebito_NaoDeveRegistrarNoHistoricoDeMovimentacoes()
    {
        var (clienteId, veiculoId, insumoId, servicoId) = await CenarioOrdemServico.CriarCadastrosAsync(_fixture, quantidadeDisponivel: 6);
        var ordemA = Guid.NewGuid();
        var ordemB = Guid.NewGuid();
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemA, clienteId, veiculoId, servicoId);
        await CenarioOrdemServico.AbrirOrdemServicoAsync(_fixture, ordemB, clienteId, veiculoId, servicoId);
        Assert.Equal(ResultadoProcessamentoEvento.Processado, await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemA));

        // Estoque consumido pela ordem A (falha do debito, os.falha) e OS desconhecida: nenhum dos dois vai para o historico
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, ordemB));
        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, await CenarioOrdemServico.AprovarPagamentoAsync(_fixture, Guid.NewGuid()));

        var debito = Assert.Single(_fixture.HistoricoEstoque.Movimentacoes, movimentacao => movimentacao.Tipo == "DebitoOrdemServico");
        Assert.Equal(insumoId, debito.InsumoId);
        Assert.Equal(ordemA, debito.OrdemServicoId);
        Assert.Equal((-4, 6, 2), (debito.Quantidade, debito.SaldoAnterior, debito.SaldoNovo));
    }

    [Fact]
    public async Task EventoNaoTratado_DeveSerIgnorado()
    {
        var resultado = await _fixture.CriarEventosController().ProcessarAsync(nameof(OrdemServicoValidada), "{}", CancellationToken.None);

        Assert.Equal(ResultadoProcessamentoEvento.Ignorado, resultado);
    }
}
