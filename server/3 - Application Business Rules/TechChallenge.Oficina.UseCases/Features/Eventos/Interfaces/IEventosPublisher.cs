namespace TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;

// Porta de saida para os eventos de integracao com o OS Service, o Billing e o Execucao e Producao (coreografia via Amazon SQS). O tipo do evento no envelope e o nome da classe do contrato.
public interface IEventosPublisher
{
    Task PublicarAsync<TEvento>(TEvento evento, Guid ordemServicoId, CancellationToken cancellationToken = default) where TEvento : class;
}
