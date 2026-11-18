using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

namespace TechChallenge.Oficina.UseCases.Features.OrdensServico.UseCases;

// Este facade agrupa os servicos auxiliares (estoque) e as integracoes de saida (eventos SQS e email) do fluxo de referencia da ordem de servico para reduzir a quantidade de dependencias injetadas no OrdemServicoReferenciaUseCases e atender a regra S107 do SonarQube sem alterar o padrao arquitetural atual.
public sealed class OrdemServicoReferenciaUseCasesFacade : IOrdemServicoReferenciaUseCasesFacade
{
    public OrdemServicoReferenciaUseCasesFacade(
        IEstoqueUseCases estoqueUseCases,
        IEventosPublisher eventosPublisher,
        IOrdemServicoStatusEmailSender ordemServicoStatusEmailSender)
    {
        EstoqueUseCases = estoqueUseCases;
        EventosPublisher = eventosPublisher;
        OrdemServicoStatusEmailSender = ordemServicoStatusEmailSender;
    }

    public IEstoqueUseCases EstoqueUseCases { get; }

    public IEventosPublisher EventosPublisher { get; }

    public IOrdemServicoStatusEmailSender OrdemServicoStatusEmailSender { get; }
}
