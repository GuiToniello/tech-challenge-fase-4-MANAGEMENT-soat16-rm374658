using TechChallenge.Oficina.UseCases.Features.Eventos;
using TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;
using TechChallenge.Oficina.UseCases.Features.OrdensServico.Commands;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

public interface IOrdemServicoReferenciaUseCases
{
    Task<ResultadoProcessamentoEvento> RegistrarAsync(RegistrarOrdemServicoReferenciaCommand command, CancellationToken cancellationToken = default);
    Task<ResultadoProcessamentoEvento> AtualizarStatusAsync(OrdemServicoStatusAlterado evento, CancellationToken cancellationToken = default);
    Task<ResultadoProcessamentoEvento> RemoverAsync(OrdemServicoExcluida evento, CancellationToken cancellationToken = default);
}
