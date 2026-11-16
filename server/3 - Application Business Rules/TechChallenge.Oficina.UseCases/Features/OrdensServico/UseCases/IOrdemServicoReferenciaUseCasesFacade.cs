using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

public interface IOrdemServicoReferenciaUseCasesFacade
{
    IEventosPublisher EventosPublisher { get; }
    IOrdemServicoStatusEmailSender OrdemServicoStatusEmailSender { get; }
}
