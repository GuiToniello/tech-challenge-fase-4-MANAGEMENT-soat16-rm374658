using TechChallenge.Oficina.UseCases.Features.Insumos.Commands;
using TechChallenge.Oficina.UseCases.Features.Insumos.Queries;
using TechChallenge.Oficina.UseCases.Features.Insumos.ViewModels;

namespace TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

public interface IInsumoUseCases
{
    Task<InsumoViewModel> CriarAsync(CriarInsumoCommand command, CancellationToken cancellationToken = default);
    Task<InsumoViewModel> AtualizarAsync(AtualizarInsumoCommand command, CancellationToken cancellationToken = default);
    Task<InsumoViewModel> ObterPorIdAsync(ObterInsumoPorIdQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<InsumoViewModel>> ListarAsync(ListarInsumosQuery query, CancellationToken cancellationToken = default);
    Task ExcluirAsync(ExcluirInsumoCommand command, CancellationToken cancellationToken = default);

    // Historico de movimentacoes de estoque do insumo (DynamoDB) em ordem cronologica; vazio quando nao ha registros
    Task<IReadOnlyCollection<MovimentacaoEstoqueViewModel>> ListarMovimentacoesAsync(ListarMovimentacoesEstoqueQuery query, CancellationToken cancellationToken = default);
}
