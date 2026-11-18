using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

public interface IOrdemServicoReferenciaUseCasesFacade
{
    IEstoqueUseCases EstoqueUseCases { get; }
    IEventosPublisher EventosPublisher { get; }
    IOrdemServicoStatusEmailSender OrdemServicoStatusEmailSender { get; }
}
