using TechChallenge.Oficina.UseCases.Features.Orcamentos.Commands;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.Queries;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

public interface IOrcamentoUseCases
{
    Task<OrcamentoViewModel> ObterPorOrdemServicoIdAsync(ObterOrcamentoPorOrdemServicoIdQuery query, CancellationToken cancellationToken = default);
    Task<OrcamentoViewModel> GerarAsync(GerarOrcamentoCommand command, CancellationToken cancellationToken = default);
    Task EnviarPorEmailAsync(EnviarOrcamentoCommand command, CancellationToken cancellationToken = default);
    Task<OrcamentoViewModel> AprovarAsync(AprovarOrcamentoCommand command, CancellationToken cancellationToken = default);
    Task<OrcamentoViewModel> RecusarAsync(RecusarOrcamentoCommand command, CancellationToken cancellationToken = default);
}
