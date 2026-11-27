using TechChallenge.Oficina.UseCases.Features.Insumos.Commands;
using TechChallenge.Oficina.UseCases.Features.Insumos.ViewModels;
using TechChallenge.Oficina.Controllers.Features.Insumos;

namespace TechChallenge.Oficina.Management.API.Features.Insumos
{
    public static class InsumoExtensions
    {
        public static RouteGroupBuilder MapInsumoEndpoints(
            this IEndpointRouteBuilder routes)
        {
            var group = routes
                .MapGroup("/api/insumos")
                .WithTags("Insumos");

            group.MapPost(
                string.Empty,
                (
                    IInsumoController insumoController,
                    CriarInsumoCommand command,
                    CancellationToken cancellationToken
                ) => insumoController.Post(command, cancellationToken))
                .Produces<InsumoViewModel>(StatusCodes.Status201Created)
                .Produces(StatusCodes.Status400BadRequest);

            group.MapGet(
                "/{id:guid}",
                (
                    IInsumoController insumoController,
                    Guid id,
                    CancellationToken cancellationToken
                ) => insumoController.GetById(id, cancellationToken))
                .WithName("GetInsumoById")
                .Produces<InsumoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status404NotFound);

            // Historico de movimentacoes de estoque do insumo (DynamoDB), em ordem cronologica: cadastro, ajustes e debitos por ordem de servico.
            // Nao consulta o PostgreSQL: insumo excluido ou desconhecido tambem responde 200, com o que houver no historico (ou lista vazia)
            group.MapGet(
                "/{id:guid}/movimentacoes",
                (
                    IInsumoController insumoController,
                    Guid id,
                    CancellationToken cancellationToken
                ) => insumoController.GetMovimentacoes(id, cancellationToken))
                .Produces<IReadOnlyCollection<MovimentacaoEstoqueViewModel>>(StatusCodes.Status200OK);

            group.MapGet(
                string.Empty,
                (
                    IInsumoController insumoController,
                    CancellationToken cancellationToken
                ) => insumoController.Get(cancellationToken))
                .Produces<IReadOnlyCollection<InsumoViewModel>>(StatusCodes.Status200OK);

            group.MapPut(
                string.Empty,
                (
                    IInsumoController insumoController,
                    AtualizarInsumoCommand command,
                    CancellationToken cancellationToken
                ) => insumoController.Put(command, cancellationToken))
                .Produces<InsumoViewModel>(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status404NotFound);

            group.MapDelete(
                "/{id:guid}",
                (
                    IInsumoController insumoController,
                    Guid id,
                    CancellationToken cancellationToken
                ) => insumoController.Delete(id, cancellationToken))
                .Produces(StatusCodes.Status204NoContent)
                .Produces(StatusCodes.Status404NotFound);

            return group;
        }
    }
}
