using TechChallenge.Oficina.Entities.Features.Orcamentos;

namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

public interface IOrcamentoGateway
{
    Task AdicionarAsync(Orcamento orcamento, CancellationToken cancellationToken = default);
    Task AtualizarAsync(Orcamento orcamento, CancellationToken cancellationToken = default);
    Task<Orcamento?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);
    Task RemoverAsync(Orcamento orcamento, CancellationToken cancellationToken = default);
}
