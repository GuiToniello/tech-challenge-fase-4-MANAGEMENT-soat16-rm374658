namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

public interface IOrdemServicoStatusEmailSender
{
    Task EnviarStatusAlteradoAsync(
        NotificacaoStatusOrdemServico notificacao,
        string emailDestino,
        CancellationToken cancellationToken = default);
}
