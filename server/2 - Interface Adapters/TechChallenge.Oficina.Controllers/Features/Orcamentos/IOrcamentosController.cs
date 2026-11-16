namespace TechChallenge.Oficina.Controllers.Features.Orcamentos
{
    public interface IOrcamentosController
    {
        Task<object> GetById(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> Gerar(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> Enviar(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> Aprovar(Guid ordemServicoId, CancellationToken cancellationToken);

        Task<object> Recusar(Guid ordemServicoId, CancellationToken cancellationToken);
    }
}
