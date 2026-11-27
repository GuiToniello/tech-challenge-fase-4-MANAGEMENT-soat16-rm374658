using AutoMapper;
using Moq;
using TechChallenge.Oficina.UseCases.Features.Insumos.Commands;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.Queries;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;
using TechChallenge.Oficina.UseCases.Features.Insumos.ViewModels;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Insumos;
using Xunit;

namespace TechChallenge.Oficina.UseCases.Tests.Features.Insumos;

public sealed class InsumoUseCasesTests
{
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<IInsumoGateway> _repositoryMock = new();
    private readonly Mock<IHistoricoEstoqueGateway> _historicoEstoqueGatewayMock = new();

    private InsumoUseCases CriarService() => new(_mapperMock.Object, _repositoryMock.Object, _historicoEstoqueGatewayMock.Object);

    private static Insumo CriarInsumo() => Insumo.Criar("Óleo 5W30", "Bosch", 10, 29.9m);

    private static InsumoViewModel MapearViewModel(Insumo insumo) =>
        new() { Id = insumo.Id, Nome = insumo.Nome, Fabricante = insumo.Fabricante, QuantidadeDisponivel = insumo.QuantidadeDisponivel, ValorUnitario = insumo.ValorUnitario };

    private void ConfigurarMapeamentoDoInsumo() =>
        _mapperMock.Setup(m => m.Map<InsumoViewModel>(It.IsAny<object>())).Returns((object source) => MapearViewModel((Insumo)source));

    private void ConfigurarMapeamentoDasMovimentacoes() =>
        _mapperMock.Setup(m => m.Map<IReadOnlyCollection<MovimentacaoEstoqueViewModel>>(It.IsAny<object>()))
            .Returns((object source) => ((IEnumerable<MovimentacaoEstoque>)source).Select(movimentacao => new MovimentacaoEstoqueViewModel
            {
                OcorridoEm = movimentacao.OcorridoEm,
                Tipo = movimentacao.Tipo,
                Origem = movimentacao.Origem,
                Quantidade = movimentacao.Quantidade,
                SaldoAnterior = movimentacao.SaldoAnterior,
                SaldoNovo = movimentacao.SaldoNovo,
                OrdemServicoId = movimentacao.OrdemServicoId
            }).ToArray());

    // Registra cada movimentacao enviada ao historico, se o PostgreSQL ja tinha gravado o insumo naquele momento e o token usado
    private List<(MovimentacaoEstoque Movimentacao, bool GravadoAntes, CancellationToken Token)> CapturarMovimentacoes(Func<bool> gravado)
    {
        var registros = new List<(MovimentacaoEstoque Movimentacao, bool GravadoAntes, CancellationToken Token)>();

        _historicoEstoqueGatewayMock
            .Setup(h => h.RegistrarAsync(It.IsAny<MovimentacaoEstoque>(), It.IsAny<CancellationToken>()))
            .Callback<MovimentacaoEstoque, CancellationToken>((movimentacao, token) => registros.Add((movimentacao, gravado(), token)))
            .Returns(Task.CompletedTask);

        return registros;
    }

    [Fact]
    public async Task CriarAsync_DeveAdicionarERetornarViewModel()
    {
        var service = CriarService();
        var command = new CriarInsumoCommand { Nome = "Óleo 5W30", Fabricante = "Bosch", QuantidadeDisponivel = 8, ValorUnitario = 19.9m };

        _mapperMock.Setup(m => m.Map<InsumoViewModel>(It.IsAny<object>())).Returns((object source) => MapearViewModel((Insumo)source));

        var resultado = await service.CriarAsync(command);

        Assert.Equal("Óleo 5W30", resultado.Nome);
        _repositoryMock.Verify(r => r.AdicionarAsync(It.IsAny<Insumo>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CriarAsync_DeveRegistrarCadastroComOSaldoInicialDepoisDeGravar()
    {
        var service = CriarService();
        var command = new CriarInsumoCommand { Nome = "Óleo 5W30", Fabricante = "Bosch", QuantidadeDisponivel = 8, ValorUnitario = 19.9m };
        Insumo? adicionado = null;
        _repositoryMock
            .Setup(r => r.AdicionarAsync(It.IsAny<Insumo>(), It.IsAny<CancellationToken>()))
            .Callback<Insumo, CancellationToken>((insumo, _) => adicionado = insumo)
            .Returns(Task.CompletedTask);
        var registros = CapturarMovimentacoes(() => adicionado is not null);
        ConfigurarMapeamentoDoInsumo();
        using var cancelamento = new CancellationTokenSource();

        await service.CriarAsync(command, cancelamento.Token);

        // Depois do PostgreSQL e sem o token da requisicao
        var (movimentacao, gravadoAntes, token) = Assert.Single(registros);
        Assert.True(gravadoAntes);
        Assert.Equal(CancellationToken.None, token);
        Assert.Equal(adicionado!.Id, movimentacao.InsumoId);
        Assert.Equal("Cadastro", movimentacao.Tipo);
        Assert.Equal("acao", movimentacao.Origem);
        Assert.Equal(8, movimentacao.Quantidade);
        Assert.Equal(0, movimentacao.SaldoAnterior);
        Assert.Equal(8, movimentacao.SaldoNovo);
        Assert.Null(movimentacao.OrdemServicoId);
        Assert.Equal(DateTimeKind.Utc, movimentacao.OcorridoEm.Kind);
    }

    [Fact]
    public async Task CriarAsync_DeveLancarDomainException_QuandoDadosInvalidos()
    {
        var service = CriarService();
        var command = new CriarInsumoCommand { Nome = "", Fabricante = "Bosch", QuantidadeDisponivel = 8, ValorUnitario = 19.9m };

        var action = async () => await service.CriarAsync(command);

        await Assert.ThrowsAsync<DomainException>(action);
        _repositoryMock.Verify(r => r.AdicionarAsync(It.IsAny<Insumo>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task AtualizarAsync_DeveAtualizarQuandoInsumoExiste()
    {
        var service = CriarService();
        var insumo = CriarInsumo();
        var command = new AtualizarInsumoCommand { Id = insumo.Id, Nome = "Filtro de Ar", Fabricante = "Fram", QuantidadeDisponivel = 20, ValorUnitario = 45.5m };

        _repositoryMock.Setup(r => r.ObterPorIdAsync(insumo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(insumo);
        _mapperMock.Setup(m => m.Map<InsumoViewModel>(It.IsAny<object>())).Returns((object source) => MapearViewModel((Insumo)source));

        var resultado = await service.AtualizarAsync(command);

        Assert.Equal("Filtro de Ar", resultado.Nome);
        _repositoryMock.Verify(r => r.AtualizarAsync(insumo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(25, 15)]
    [InlineData(4, -6)]
    public async Task AtualizarAsync_DeveRegistrarAjusteComAVariacaoDoSaldo_QuandoQuantidadeMuda(int quantidadeNova, int variacaoEsperada)
    {
        var service = CriarService();
        var insumo = CriarInsumo();
        var atualizado = false;
        _repositoryMock.Setup(r => r.ObterPorIdAsync(insumo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(insumo);
        _repositoryMock
            .Setup(r => r.AtualizarAsync(insumo, It.IsAny<CancellationToken>()))
            .Callback(() => atualizado = true)
            .Returns(Task.CompletedTask);
        var registros = CapturarMovimentacoes(() => atualizado);
        ConfigurarMapeamentoDoInsumo();
        var command = new AtualizarInsumoCommand { Id = insumo.Id, Nome = insumo.Nome, Fabricante = insumo.Fabricante, QuantidadeDisponivel = quantidadeNova, ValorUnitario = insumo.ValorUnitario };
        using var cancelamento = new CancellationTokenSource();

        await service.AtualizarAsync(command, cancelamento.Token);

        // Saldo de 10 para o novo valor, registrado depois do PostgreSQL e sem o token da requisicao
        var (movimentacao, gravadoAntes, token) = Assert.Single(registros);
        Assert.True(gravadoAntes);
        Assert.Equal(CancellationToken.None, token);
        Assert.Equal(insumo.Id, movimentacao.InsumoId);
        Assert.Equal("Ajuste", movimentacao.Tipo);
        Assert.Equal("acao", movimentacao.Origem);
        Assert.Equal(variacaoEsperada, movimentacao.Quantidade);
        Assert.Equal(10, movimentacao.SaldoAnterior);
        Assert.Equal(quantidadeNova, movimentacao.SaldoNovo);
        Assert.Null(movimentacao.OrdemServicoId);
    }

    [Fact]
    public async Task AtualizarAsync_NaoDeveRegistrarAjuste_QuandoSaldoNaoMuda()
    {
        var service = CriarService();
        var insumo = CriarInsumo();
        _repositoryMock.Setup(r => r.ObterPorIdAsync(insumo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(insumo);
        ConfigurarMapeamentoDoInsumo();

        // Muda nome, fabricante e valor, mas mantem as 10 unidades
        await service.AtualizarAsync(new AtualizarInsumoCommand { Id = insumo.Id, Nome = "Filtro de Ar", Fabricante = "Fram", QuantidadeDisponivel = 10, ValorUnitario = 45.5m });

        _repositoryMock.Verify(r => r.AtualizarAsync(insumo, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task AtualizarAsync_NaoDeveGravarNemRegistrarAjuste_QuandoDadosInvalidos()
    {
        var service = CriarService();
        var insumo = CriarInsumo();
        _repositoryMock.Setup(r => r.ObterPorIdAsync(insumo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(insumo);
        var command = new AtualizarInsumoCommand { Id = insumo.Id, Nome = "", Fabricante = "Fram", QuantidadeDisponivel = 25, ValorUnitario = 45.5m };

        var action = async () => await service.AtualizarAsync(command);

        await Assert.ThrowsAsync<DomainException>(action);
        _repositoryMock.Verify(r => r.AtualizarAsync(It.IsAny<Insumo>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task AtualizarAsync_DeveLancarQuandoInsumoNaoEncontrado()
    {
        var service = CriarService();
        var command = new AtualizarInsumoCommand { Id = Guid.NewGuid(), Nome = "Filtro", Fabricante = "Fram", QuantidadeDisponivel = 20, ValorUnitario = 45.5m };

        _repositoryMock.Setup(r => r.ObterPorIdAsync(command.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Insumo?)null);

        var action = async () => await service.AtualizarAsync(command);

        var exception = await Assert.ThrowsAsync<KeyNotFoundException>(action);
        Assert.Equal("Insumo não encontrado.", exception.Message);
        Assert.Empty(_historicoEstoqueGatewayMock.Invocations);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveLancarQuandoNaoEncontrado()
    {
        var service = CriarService();
        var query = new ObterInsumoPorIdQuery { Id = Guid.NewGuid() };

        _repositoryMock.Setup(r => r.ObterPorIdAsync(query.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Insumo?)null);

        var action = async () => await service.ObterPorIdAsync(query);

        await Assert.ThrowsAsync<KeyNotFoundException>(action);
    }

    [Fact]
    public async Task ListarAsync_DeveRetornarColecaoMapeada()
    {
        var service = CriarService();
        var insumos = new[] { CriarInsumo() };

        _repositoryMock.Setup(r => r.ListarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(insumos);
        _mapperMock.Setup(m => m.Map<IReadOnlyCollection<InsumoViewModel>>(It.IsAny<object>())).Returns(insumos.Select(MapearViewModel).ToArray());

        var resultado = await service.ListarAsync(new ListarInsumosQuery());

        Assert.Single(resultado);
    }

    [Fact]
    public async Task ExcluirAsync_DeveRemoverQuandoEncontrado()
    {
        var service = CriarService();
        var insumo = CriarInsumo();

        _repositoryMock.Setup(r => r.ObterPorIdAsync(insumo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(insumo);

        await service.ExcluirAsync(new ExcluirInsumoCommand { Id = insumo.Id });

        _repositoryMock.Verify(r => r.RemoverAsync(insumo, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListarMovimentacoesAsync_DeveRetornarAsMovimentacoesEmOrdemCronologicaSemConsultarOInsumo()
    {
        var service = CriarService();
        var insumoId = Guid.NewGuid();
        var ordemServicoId = Guid.NewGuid();
        var inicio = new DateTime(2026, 11, 27, 8, 0, 0, DateTimeKind.Utc);
        var cadastro = new MovimentacaoEstoque
        {
            InsumoId = insumoId,
            OcorridoEm = inicio,
            Tipo = TiposMovimentacaoEstoque.Cadastro,
            Origem = OrigensMovimentacaoEstoque.Acao,
            Quantidade = 10,
            SaldoAnterior = 0,
            SaldoNovo = 10
        };
        var debito = cadastro with
        {
            OcorridoEm = inicio.AddMinutes(5),
            Tipo = TiposMovimentacaoEstoque.DebitoOrdemServico,
            Origem = OrigensMovimentacaoEstoque.Evento,
            Quantidade = -4,
            SaldoAnterior = 10,
            SaldoNovo = 6,
            OrdemServicoId = ordemServicoId
        };
        using var cancelamento = new CancellationTokenSource();
        _historicoEstoqueGatewayMock.Setup(h => h.ListarAsync(insumoId, cancelamento.Token)).ReturnsAsync([debito, cadastro]);
        ConfigurarMapeamentoDasMovimentacoes();

        var movimentacoes = await service.ListarMovimentacoesAsync(new ListarMovimentacoesEstoqueQuery { Id = insumoId }, cancelamento.Token);

        Assert.Equal(["Cadastro", "DebitoOrdemServico"], movimentacoes.Select(movimentacao => movimentacao.Tipo));
        Assert.Equal([inicio, inicio.AddMinutes(5)], movimentacoes.Select(movimentacao => movimentacao.OcorridoEm));
        Assert.Null(movimentacoes.First().OrdemServicoId);
        var ultima = movimentacoes.Last();
        Assert.Equal("evento", ultima.Origem);
        Assert.Equal(-4, ultima.Quantidade);
        Assert.Equal(10, ultima.SaldoAnterior);
        Assert.Equal(6, ultima.SaldoNovo);
        Assert.Equal(ordemServicoId, ultima.OrdemServicoId);
        // O historico e so do DynamoDB: insumo excluido ou desconhecido nao impede a consulta
        Assert.Empty(_repositoryMock.Invocations);
    }

    [Fact]
    public async Task ListarMovimentacoesAsync_DeveRetornarListaVazia_QuandoNaoHaMovimentacoes()
    {
        var service = CriarService();
        var insumoId = Guid.NewGuid();
        _historicoEstoqueGatewayMock.Setup(h => h.ListarAsync(insumoId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        ConfigurarMapeamentoDasMovimentacoes();

        var movimentacoes = await service.ListarMovimentacoesAsync(new ListarMovimentacoesEstoqueQuery { Id = insumoId });

        Assert.Empty(movimentacoes);
        Assert.Empty(_repositoryMock.Invocations);
    }
}
