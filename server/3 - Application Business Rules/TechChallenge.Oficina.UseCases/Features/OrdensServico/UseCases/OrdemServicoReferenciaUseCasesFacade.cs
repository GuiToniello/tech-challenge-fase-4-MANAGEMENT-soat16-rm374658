using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

// Este facade agrupa as integracoes de saida (eventos SQS e email) do fluxo de referencia da ordem de servico para reduzir a quantidade de dependencias injetadas no OrdemServicoReferenciaUseCases e atender a regra S107 do SonarQube sem alterar o padrao arquitetural atual.
public sealed class OrdemServicoReferenciaUseCasesFacade : IOrdemServicoReferenciaUseCasesFacade
{
    public OrdemServicoReferenciaUseCasesFacade(
        IEventosPublisher eventosPublisher,
        IOrdemServicoStatusEmailSender ordemServicoStatusEmailSender)
    {
        EventosPublisher = eventosPublisher;
        OrdemServicoStatusEmailSender = ordemServicoStatusEmailSender;
    }

    public IEventosPublisher EventosPublisher { get; }

    public IOrdemServicoStatusEmailSender OrdemServicoStatusEmailSender { get; }
}
