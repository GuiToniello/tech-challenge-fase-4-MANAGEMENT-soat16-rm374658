using TechChallenge.Oficina.Entities.Features.Orcamentos;

namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

public interface IOrcamentoEmailSender
{
    Task EnviarOrcamentoAsync(Orcamento orcamento, string emailDestino, CancellationToken cancellationToken = default);
}
