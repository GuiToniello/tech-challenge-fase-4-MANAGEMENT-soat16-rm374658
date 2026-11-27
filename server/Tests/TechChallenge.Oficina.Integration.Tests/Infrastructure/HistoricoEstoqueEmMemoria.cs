using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

namespace TechChallenge.Oficina.Integration.Tests.Infrastructure;

/// <summary>
/// Historico de movimentacoes de estoque em memoria no lugar do DynamoDB: uma lista com as movimentacoes na ordem de registro.
/// </summary>
public sealed class HistoricoEstoqueEmMemoria : IHistoricoEstoqueGateway
{
    private readonly List<MovimentacaoEstoque> _movimentacoes = [];

    public IReadOnlyList<MovimentacaoEstoque> Movimentacoes => _movimentacoes;

    public Task RegistrarAsync(MovimentacaoEstoque movimentacao, CancellationToken cancellationToken)
    {
        _movimentacoes.Add(movimentacao);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<MovimentacaoEstoque>> ListarAsync(Guid insumoId, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<MovimentacaoEstoque> movimentacoes = _movimentacoes
            .Where(movimentacao => movimentacao.InsumoId == insumoId)
            .OrderBy(movimentacao => movimentacao.OcorridoEm)
            .ToArray();

        return Task.FromResult(movimentacoes);
    }
}
