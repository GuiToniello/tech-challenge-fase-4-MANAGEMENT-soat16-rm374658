using TechChallenge.Oficina.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.Management.API.Features.Orcamentos
{
    public static class OrcamentoEndpoints
    {
        public static RouteGroupBuilder MapOrcamentoEndpoints(this IEndpointRouteBuilder routes)
        {
            var group = routes
                .MapGroup("/api/orcamentos")
                .WithTags("Orcamentos");

            group.MapGet(
                "/{ordemServicoId:guid}",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.GetById(ordemServicoId, cancellationToken))
                .WithName("GetOrcamentoByOrdemServicoId")
                .Produces<OrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound);

            group.MapPost(
                "/{ordemServicoId:guid}/gerar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Gerar(ordemServicoId, cancellationToken))
                .Produces<OrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            group.MapPost(
                "/{ordemServicoId:guid}/enviar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Enviar(ordemServicoId, cancellationToken))
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            group.MapPost(
                "/{ordemServicoId:guid}/aprovar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Aprovar(ordemServicoId, cancellationToken))
                .Produces<OrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            group.MapPost(
                "/{ordemServicoId:guid}/recusar",
                (
                    IOrcamentosController orcamentosController,
                    Guid ordemServicoId,
                    CancellationToken cancellationToken
                ) => orcamentosController.Recusar(ordemServicoId, cancellationToken))
                .Produces<OrcamentoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            return group;
        }
    }
}
