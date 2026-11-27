using System.Globalization;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.NoSql.Configuration;
using TechChallenge.Oficina.NoSql.DynamoDb;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Logging;

namespace TechChallenge.Oficina.NoSql.Features.Insumos;

// Historico de movimentacoes de estoque no DynamoDB: uma particao por insumo (InsumoId) e a chave de ordenacao "OcorridoEm#sufixo"
// (data UTC com milissegundos + id curto), em ordem cronologica e sem colisao entre movimentacoes do mesmo instante. Cada item expira em 180 dias (TTL em ExpiraEm).
// O registro e best-effort: a falha sai como integracao.falha (Warning) e a operacao segue. A consulta registra a falha e relanca.
internal sealed class HistoricoEstoqueGateway : IHistoricoEstoqueGateway
{
    public const string ChaveParticao = "InsumoId";
    public const string ChaveOrdenacao = "OcorridoEm";
    public const string AtributoExpiracao = "ExpiraEm";
    public const string AtributoTipo = "Tipo";
    public const string AtributoOrigem = "Origem";
    public const string AtributoQuantidade = "Quantidade";
    public const string AtributoSaldoAnterior = "SaldoAnterior";
    public const string AtributoSaldoNovo = "SaldoNovo";
    public const string AtributoOrdemServicoId = "OrdemServicoId";

    public static readonly TimeSpan Retencao = TimeSpan.FromDays(180);

    private const string FormatoData = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
    private const char SeparadorChaveOrdenacao = '#';
    private const int TamanhoSufixo = 8;

    private readonly IDynamoDbClient _dynamoDbClient;
    private readonly DynamoDbSettings _settings;
    private readonly ILogger<HistoricoEstoqueGateway> _logger;

    public HistoricoEstoqueGateway(IDynamoDbClient dynamoDbClient, DynamoDbSettings settings, ILogger<HistoricoEstoqueGateway> logger)
    {
        _dynamoDbClient = dynamoDbClient;
        _settings = settings;
        _logger = logger;
    }

    public async Task RegistrarAsync(MovimentacaoEstoque movimentacao, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movimentacao);

        try
        {
            await _dynamoDbClient.PutItemAsync(_settings.TabelaHistoricoEstoque, CriarItem(movimentacao), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Historico complementar: o saldo ja esta gravado no PostgreSQL (fonte da verdade), entao a falha nao interrompe o cadastro, o ajuste nem o debito
            _logger.LogWarning(
                OrdemServicoLogEventos.IntegracaoFalha,
                exception,
                "Falha na integracao {Integracao} durante a operacao {Operacao} da movimentacao {Tipo} do insumo {InsumoId}; o historico de estoque fica sem este registro.",
                OrdemServicoLogEventos.Integracoes.DynamoDb,
                OrdemServicoLogEventos.Operacoes.RegistroHistorico,
                movimentacao.Tipo,
                movimentacao.InsumoId);
        }
    }

    public async Task<IReadOnlyCollection<MovimentacaoEstoque>> ListarAsync(Guid insumoId, CancellationToken cancellationToken)
    {
        try
        {
            var itens = await _dynamoDbClient.QueryAsync(_settings.TabelaHistoricoEstoque, ChaveParticao, insumoId.ToString("D"), cancellationToken);
            return itens.Select(Mapear).ToArray();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                OrdemServicoLogEventos.IntegracaoFalha,
                exception,
                "Falha na integracao {Integracao} durante a operacao {Operacao} do insumo {InsumoId}.",
                OrdemServicoLogEventos.Integracoes.DynamoDb,
                OrdemServicoLogEventos.Operacoes.ConsultaHistorico,
                insumoId);
            throw;
        }
    }

    // Sem dados pessoais: so o identificador do insumo, tipo, origem, quantidade, saldos e, no debito, o identificador da OS
    internal static Dictionary<string, AttributeValue> CriarItem(MovimentacaoEstoque movimentacao)
    {
        var ocorridoEm = ParaUtc(movimentacao.OcorridoEm);

        var item = new Dictionary<string, AttributeValue>
        {
            [ChaveParticao] = new AttributeValue { S = movimentacao.InsumoId.ToString("D") },
            [ChaveOrdenacao] = new AttributeValue { S = CriarChaveOrdenacao(ocorridoEm) },
            [AtributoExpiracao] = new AttributeValue { N = new DateTimeOffset(ocorridoEm).Add(Retencao).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) },
            [AtributoTipo] = new AttributeValue { S = movimentacao.Tipo },
            [AtributoOrigem] = new AttributeValue { S = movimentacao.Origem },
            [AtributoQuantidade] = new AttributeValue { N = movimentacao.Quantidade.ToString(CultureInfo.InvariantCulture) },
            [AtributoSaldoAnterior] = new AttributeValue { N = movimentacao.SaldoAnterior.ToString(CultureInfo.InvariantCulture) },
            [AtributoSaldoNovo] = new AttributeValue { N = movimentacao.SaldoNovo.ToString(CultureInfo.InvariantCulture) }
        };

        // A OS so entra no debito por ordem de servico (cadastro e ajuste sao acoes na API, sem OS)
        if (movimentacao.OrdemServicoId is { } ordemServicoId)
        {
            item[AtributoOrdemServicoId] = new AttributeValue { S = ordemServicoId.ToString("D") };
        }

        return item;
    }

    internal static string CriarChaveOrdenacao(DateTime ocorridoEmUtc)
    {
        var sufixo = Guid.NewGuid().ToString("N")[..TamanhoSufixo];
        return $"{ocorridoEmUtc.ToString(FormatoData, CultureInfo.InvariantCulture)}{SeparadorChaveOrdenacao}{sufixo}";
    }

    internal static MovimentacaoEstoque Mapear(Dictionary<string, AttributeValue> item)
    {
        var data = item[ChaveOrdenacao].S.Split(SeparadorChaveOrdenacao)[0];

        return new MovimentacaoEstoque
        {
            InsumoId = Guid.Parse(item[ChaveParticao].S),
            OcorridoEm = DateTime.ParseExact(data, FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
            Tipo = item[AtributoTipo].S,
            Origem = item[AtributoOrigem].S,
            Quantidade = LerNumero(item[AtributoQuantidade].N),
            SaldoAnterior = LerNumero(item[AtributoSaldoAnterior].N),
            SaldoNovo = LerNumero(item[AtributoSaldoNovo].N),
            OrdemServicoId = item.TryGetValue(AtributoOrdemServicoId, out var ordemServicoId) ? Guid.Parse(ordemServicoId.S) : null
        };
    }

    // Quantidade com sinal (negativa no debito e no ajuste para baixo)
    private static int LerNumero(string valor)
    {
        return int.Parse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    // Data sem fuso (Unspecified) e tratada como UTC, como o restante do servico (DateTime.UtcNow)
    private static DateTime ParaUtc(DateTime data)
    {
        return data.Kind == DateTimeKind.Local ? data.ToUniversalTime() : DateTime.SpecifyKind(data, DateTimeKind.Utc);
    }
}
