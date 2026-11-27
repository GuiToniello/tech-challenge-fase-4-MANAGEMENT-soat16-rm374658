using TechChallenge.Oficina.Entities.Features.Servicos;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

namespace TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

public interface IEstoqueUseCases
{
    Task<bool> PossuiDisponibilidadeAsync(IReadOnlyCollection<Servico> servicos, CancellationToken cancellationToken = default);
    // Retorna o debito de cada insumo com os saldos de antes e depois, para o historico de movimentacoes de estoque
    Task<IReadOnlyCollection<DebitoInsumo>> DebitarEstoqueParaOrdemServicoAsync(IReadOnlyCollection<Servico> servicos, CancellationToken cancellationToken = default);
}
