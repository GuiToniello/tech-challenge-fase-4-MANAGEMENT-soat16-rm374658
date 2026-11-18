using TechChallenge.Oficina.Entities.Features.Servicos;

namespace TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

public interface IEstoqueUseCases
{
    Task<bool> PossuiDisponibilidadeAsync(IReadOnlyCollection<Servico> servicos, CancellationToken cancellationToken = default);
    Task DebitarEstoqueParaOrdemServicoAsync(IReadOnlyCollection<Servico> servicos, CancellationToken cancellationToken = default);
}
