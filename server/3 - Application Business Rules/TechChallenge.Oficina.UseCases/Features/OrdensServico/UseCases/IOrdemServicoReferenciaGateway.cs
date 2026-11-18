using TechChallenge.Oficina.Entities.Features.OrdensServico;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

public interface IOrdemServicoReferenciaGateway
{
    Task AdicionarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default);
    Task AtualizarAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default);
    Task<OrdemServicoReferencia?> ObterPorIdAsync(Guid ordemServicoId, CancellationToken cancellationToken = default);
    Task RemoverAsync(OrdemServicoReferencia referencia, CancellationToken cancellationToken = default);

    // Executa a operacao (que pode gravar por varios gateways) em uma unica transacao do banco: grava tudo ou nada
    Task ExecutarEmTransacaoAsync(Func<Task> operacao, CancellationToken cancellationToken = default);
}
