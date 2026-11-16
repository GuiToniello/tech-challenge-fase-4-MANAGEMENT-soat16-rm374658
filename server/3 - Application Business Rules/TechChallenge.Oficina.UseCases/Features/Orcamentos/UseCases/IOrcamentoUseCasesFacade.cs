using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

public interface IOrcamentoUseCasesFacade
{
    IEstoqueUseCases EstoqueUseCases { get; }
    IOrcamentoEmailSender OrcamentoEmailSender { get; }
    IEventosPublisher EventosPublisher { get; }
}
