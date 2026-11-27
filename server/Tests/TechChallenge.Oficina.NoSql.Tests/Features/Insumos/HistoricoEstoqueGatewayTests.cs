using System.Text.RegularExpressions;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using Moq;
using TechChallenge.Oficina.NoSql.Configuration;
using TechChallenge.Oficina.NoSql.DynamoDb;
using TechChallenge.Oficina.NoSql.Features.Insumos;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;
using Xunit;

namespace TechChallenge.Oficina.NoSql.Tests.Features.Insumos;

public sealed class HistoricoEstoqueGatewayTests
{
    private const string Tabela = "techchallenge-oficina-management-estoque-historico";

    private static readonly DateTime OcorridoEm = new(2026, 11, 27, 10, 15, 30, 123, DateTimeKind.Utc);

    private readonly Mock<IDynamoDbClient> _dynamoDbClientMock = new();
    private readonly Mock<ILogger<HistoricoEstoqueGateway>> _loggerMock = new();
    private readonly Guid _insumoId = Guid.NewGuid();
    private readonly Guid _ordemServicoId = Guid.NewGuid();

    private HistoricoEstoqueGateway CriarGateway() =>
        new(_dynamoDbClientMock.Object, new DynamoDbSettings { TabelaHistoricoEstoque = Tabela }, _loggerMock.Object);

    // Pagamento aprovado no Billing: 2 unidades do insumo debitadas para a OS (de 10 para 8)
    private MovimentacaoEstoque CriarDebito() => new()
    {
        InsumoId = _insumoId,
        OcorridoEm = OcorridoEm,
        Tipo = TiposMovimentacaoEstoque.DebitoOrdemServico,
        Origem = OrigensMovimentacaoEstoque.Evento,
        Quantidade = -2,
        SaldoAnterior = 10,
        SaldoNovo = 8,
        OrdemServicoId = _ordemServicoId
    };

    // Insumo cadastrado pela API com 10 unidades: sem OS
    private MovimentacaoEstoque CriarCadastro() => CriarDebito() with
    {
        Tipo = TiposMovimentacaoEstoque.Cadastro,
        Origem = OrigensMovimentacaoEstoque.Acao,
        Quantidade = 10,
        SaldoAnterior = 0,
        SaldoNovo = 10,
        OrdemServicoId = null
    };

    // Saldo reduzido de 8 para 5 na atualizacao do insumo pela API
    private MovimentacaoEstoque CriarAjusteParaBaixo() => CriarCadastro() with
    {
        Tipo = TiposMovimentacaoEstoque.Ajuste,
        Quantidade = -3,
        SaldoAnterior = 8,
        SaldoNovo = 5
    };

    private (LogLevel Nivel, IReadOnlyDictionary<string, object?> Propriedades, Exception? Excecao) ObterRegistroDeFalha()
    {
        var invocacao = Assert.Single(_loggerMock.Invocations, chamada => chamada.Method.Name == nameof(ILogger.Log) && ((EventId)chamada.Arguments[1]).Id == OrdemServicoLogEventos.IntegracaoFalha.Id);
        var propriedades = ((IEnumerable<KeyValuePair<string, object?>>)invocacao.Arguments[2]).ToDictionary(propriedade => propriedade.Key, propriedade => propriedade.Value);

        return ((LogLevel)invocacao.Arguments[0], propriedades, (Exception?)invocacao.Arguments[3]);
    }

    [Fact]
    public async Task RegistrarAsync_DeveGravarItemComChavesTtlEAtributosDoContrato()
    {
        Dictionary<string, AttributeValue>? gravado = null;
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(Tabela, It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Dictionary<string, AttributeValue>, CancellationToken>((_, item, _) => gravado = item)
            .Returns(Task.CompletedTask);

        await CriarGateway().RegistrarAsync(CriarDebito(), CancellationToken.None);

        Assert.NotNull(gravado);
        Assert.Equal(_insumoId.ToString("D"), gravado["InsumoId"].S);
        Assert.Matches(new Regex("^2026-11-27T10:15:30\\.123Z#[0-9a-f]{8}$"), gravado["OcorridoEm"].S);
        Assert.Equal(new DateTimeOffset(OcorridoEm).AddDays(180).ToUnixTimeSeconds().ToString(), gravado["ExpiraEm"].N);
        Assert.Equal("DebitoOrdemServico", gravado["Tipo"].S);
        Assert.Equal("evento", gravado["Origem"].S);
        Assert.Equal("-2", gravado["Quantidade"].N);
        Assert.Equal("10", gravado["SaldoAnterior"].N);
        Assert.Equal("8", gravado["SaldoNovo"].N);
        Assert.Equal(_ordemServicoId.ToString("D"), gravado["OrdemServicoId"].S);
        Assert.Equal(9, gravado.Count);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public void CriarItem_NaoDeveIncluirAOrdemDeServico_QuandoCadastroPelaApi()
    {
        var item = HistoricoEstoqueGateway.CriarItem(CriarCadastro());

        Assert.Equal("Cadastro", item["Tipo"].S);
        Assert.Equal("acao", item["Origem"].S);
        Assert.Equal("10", item["Quantidade"].N);
        Assert.Equal("0", item["SaldoAnterior"].N);
        Assert.Equal("10", item["SaldoNovo"].N);
        Assert.DoesNotContain("OrdemServicoId", item.Keys);
        Assert.Equal(8, item.Count);
    }

    [Fact]
    public void CriarItem_DeveGravarQuantidadeNegativa_QuandoAjusteReduzOSaldo()
    {
        var item = HistoricoEstoqueGateway.CriarItem(CriarAjusteParaBaixo());

        Assert.Equal("Ajuste", item["Tipo"].S);
        Assert.Equal("-3", item["Quantidade"].N);
        Assert.Equal("8", item["SaldoAnterior"].N);
        Assert.Equal("5", item["SaldoNovo"].N);
        Assert.DoesNotContain("OrdemServicoId", item.Keys);
    }

    [Fact]
    public void CriarItem_DeveTratarDataSemFusoComoUtc()
    {
        var semFuso = DateTime.SpecifyKind(OcorridoEm, DateTimeKind.Unspecified);

        var item = HistoricoEstoqueGateway.CriarItem(CriarCadastro() with { OcorridoEm = semFuso });

        Assert.StartsWith("2026-11-27T10:15:30.123Z#", item["OcorridoEm"].S);
    }

    [Fact]
    public void CriarChaveOrdenacao_DeveSerUnicaNoMesmoInstanteEOrdenarCronologicamente()
    {
        var primeira = HistoricoEstoqueGateway.CriarChaveOrdenacao(OcorridoEm);
        var mesmoInstante = HistoricoEstoqueGateway.CriarChaveOrdenacao(OcorridoEm);
        var seguinte = HistoricoEstoqueGateway.CriarChaveOrdenacao(OcorridoEm.AddMilliseconds(1));

        Assert.NotEqual(primeira, mesmoInstante);
        Assert.True(string.CompareOrdinal(primeira, seguinte) < 0);
        Assert.True(string.CompareOrdinal(mesmoInstante, seguinte) < 0);
    }

    [Fact]
    public void Mapear_DeveRecuperarAMovimentacaoGravada()
    {
        MovimentacaoEstoque[] movimentacoes = [CriarCadastro(), CriarAjusteParaBaixo(), CriarDebito()];

        foreach (var movimentacao in movimentacoes)
        {
            var mapeada = HistoricoEstoqueGateway.Mapear(HistoricoEstoqueGateway.CriarItem(movimentacao));

            Assert.Equal(movimentacao, mapeada);
            Assert.Equal(DateTimeKind.Utc, mapeada.OcorridoEm.Kind);
        }
    }

    [Fact]
    public async Task RegistrarAsync_DeveRegistrarWarningSemLancar_QuandoDynamoDbFalha()
    {
        var falha = new InvalidOperationException("DynamoDB indisponivel");
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        await CriarGateway().RegistrarAsync(CriarDebito(), CancellationToken.None);

        var (nivel, propriedades, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("dynamodb", propriedades["Integracao"]);
        Assert.Equal("registro_historico", propriedades["Operacao"]);
        Assert.Equal("DebitoOrdemServico", propriedades["Tipo"]);
        Assert.Equal(_insumoId, propriedades["InsumoId"]);
    }

    [Fact]
    public async Task RegistrarAsync_DeveRegistrarWarningSemLancar_QuandoTempoEsgotadoSemCancelarAOperacao()
    {
        // Os casos de uso registram com CancellationToken.None: o tempo esgotado do SDK (TaskCanceledException) tambem e so um aviso
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("Tempo esgotado"));

        await CriarGateway().RegistrarAsync(CriarCadastro(), CancellationToken.None);

        var (nivel, _, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Warning, nivel);
        Assert.IsType<TaskCanceledException>(excecao);
    }

    [Fact]
    public async Task RegistrarAsync_DevePropagarCancelamentoDoChamador()
    {
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _dynamoDbClientMock
            .Setup(c => c.PutItemAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, AttributeValue>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await CriarGateway().RegistrarAsync(CriarCadastro(), cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public async Task ListarAsync_DeveConsultarAParticaoDoInsumoEMapearOsItens()
    {
        var cadastro = CriarCadastro();
        var debito = CriarDebito() with { OcorridoEm = OcorridoEm.AddMinutes(2) };
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(Tabela, "InsumoId", _insumoId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([HistoricoEstoqueGateway.CriarItem(cadastro), HistoricoEstoqueGateway.CriarItem(debito)]);

        var movimentacoes = await CriarGateway().ListarAsync(_insumoId, CancellationToken.None);

        Assert.Equal([cadastro, debito], movimentacoes);
    }

    [Fact]
    public async Task ListarAsync_DeveRetornarVazio_QuandoInsumoSemMovimentacoes()
    {
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(Tabela, "InsumoId", _insumoId.ToString("D"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var movimentacoes = await CriarGateway().ListarAsync(_insumoId, CancellationToken.None);

        Assert.Empty(movimentacoes);
        Assert.Empty(_loggerMock.Invocations);
    }

    [Fact]
    public async Task ListarAsync_DeveRegistrarErroERelancar_QuandoDynamoDbFalha()
    {
        var falha = new InvalidOperationException("DynamoDB indisponivel");
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(falha);

        var action = async () => await CriarGateway().ListarAsync(_insumoId, CancellationToken.None);

        Assert.Same(falha, await Assert.ThrowsAsync<InvalidOperationException>(action));
        var (nivel, propriedades, excecao) = ObterRegistroDeFalha();
        Assert.Equal(LogLevel.Error, nivel);
        Assert.Same(falha, excecao);
        Assert.Equal("dynamodb", propriedades["Integracao"]);
        Assert.Equal("consulta_historico", propriedades["Operacao"]);
        Assert.Equal(_insumoId, propriedades["InsumoId"]);
    }

    [Fact]
    public async Task ListarAsync_DevePropagarCancelamentoDaRequisicaoSemRegistrarFalha()
    {
        // Cliente desconectou: o middleware trata o cancelamento (499), sem integracao.falha
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();
        _dynamoDbClientMock
            .Setup(c => c.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cancelamento.Token));

        var action = async () => await CriarGateway().ListarAsync(_insumoId, cancelamento.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(action);
        Assert.Empty(_loggerMock.Invocations);
    }
}
