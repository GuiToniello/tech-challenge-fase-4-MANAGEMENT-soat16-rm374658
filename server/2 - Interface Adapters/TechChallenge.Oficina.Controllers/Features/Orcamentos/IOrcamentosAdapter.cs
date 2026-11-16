using TechChallenge.Oficina.UseCases.Features.Orcamentos.ViewModels;

namespace TechChallenge.Oficina.Controllers.Features.Orcamentos
{
    public interface IOrcamentosAdapter
    {
        object Adapt(OrcamentosResult<OrcamentoViewModel, Exception> result);

        object Adapt(OrcamentosResult<bool, Exception> result);

        object Empty();
    }
}
