using Microsoft.AspNetCore.Http;
using TechChallenge.Oficina.Controllers.Features.Orcamentos;
using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.Adapters.Features.Orcamentos
{
    public class OrcamentosAdapter : IOrcamentosAdapter
    {
        public object Adapt(OrcamentosResult<OrcamentoViewModel, Exception> result)
        {
            if (result.Value != null)
            {
                return TypedResults.Ok(result.Value);
            }

            return CriaErro(result.Error!);
        }

        public object Adapt(OrcamentosResult<bool, Exception> result)
        {
            if (result.Value)
            {
                return TypedResults.Ok(result.Value);
            }

            return CriaErro(result.Error!);
        }

        public object Empty()
        {
            return TypedResults.NoContent();
        }

        public object CriaErro(Exception ex)
        {
            if (ex is DomainException)
            {
                return TypedResults.BadRequest(new Dictionary<string, string?> { ["message"] = ex.Message });
            }

            if (ex is KeyNotFoundException)
            {
                return TypedResults.NotFound(new Dictionary<string, string?> { ["message"] = ex.Message });
            }

            return TypedResults.Problem(ex?.Message);
        }
    }
}
