using TechChallenge.Oficina.UseCases.Features.Eventos.Interfaces;
using TechChallenge.Oficina.UseCases.Features.Insumos.UseCases;

namespace TechChallenge.Oficina.UseCases.Features.Orcamentos.UseCases;

// Este facade agrupa serviços auxiliares do fluxo de orçamento (estoque, email e eventos SQS) para reduzir a quantidade de dependências injetadas no OrcamentoUseCases e atender à regra S107 do SonarQube sem alterar o padrão arquitetural atual.
public sealed class OrcamentoUseCasesFacade : IOrcamentoUseCasesFacade
{
    public OrcamentoUseCasesFacade(
        IEstoqueUseCases estoqueUseCases,
        IOrcamentoEmailSender orcamentoEmailSender,
        IEventosPublisher eventosPublisher)
    {
        EstoqueUseCases = estoqueUseCases;
        OrcamentoEmailSender = orcamentoEmailSender;
        EventosPublisher = eventosPublisher;
    }

    public IEstoqueUseCases EstoqueUseCases { get; }

    public IOrcamentoEmailSender OrcamentoEmailSender { get; }

    public IEventosPublisher EventosPublisher { get; }
}
