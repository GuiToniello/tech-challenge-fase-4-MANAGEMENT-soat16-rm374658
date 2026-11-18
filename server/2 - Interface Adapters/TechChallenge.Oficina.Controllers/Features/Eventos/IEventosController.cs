using TechChallenge.Oficina.UseCases.Features.Eventos;

namespace TechChallenge.Oficina.Controllers.Features.Eventos
{
    // Entrada dos eventos recebidos do OS Service e do Billing: o consumidor SQS repassa o tipo e os dados (JSON) do envelope.
    public interface IEventosController
    {
        Task<ResultadoProcessamentoEvento> ProcessarAsync(string tipo, string dadosJson, CancellationToken cancellationToken);
    }
}
