using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.Orcamentos.Enums;
using TechChallenge.Oficina.Entities.Features.Servicos;

namespace TechChallenge.Oficina.Entities.Features.Orcamentos;

// Raiz de agregado do orcamento: o Id e o mesmo da ordem de servico (OS Service), ja que cada OS possui no maximo um orcamento.
public sealed class Orcamento
{
    private readonly List<OrcamentoServico> _servicos = [];

    public Guid Id { get; private set; }
    public Guid OrdemServicoId => Id;
    public DateTime DataGeracao { get; private set; }
    public decimal ValorTotal { get; private set; }
    public StatusOrcamento Status { get; private set; }
    public IReadOnlyCollection<OrcamentoServico> Servicos => _servicos.AsReadOnly();

    private Orcamento()
    {
    }

    private Orcamento(Guid ordemServicoId, IReadOnlyCollection<Servico> servicos, DateTime dataGeracao)
    {
        if (ordemServicoId == Guid.Empty)
        {
            throw new DomainException("A ordem de servico do orcamento e obrigatoria.");
        }

        Id = ordemServicoId;
        Status = StatusOrcamento.Gerado;

        DefinirServicos(servicos, dataGeracao);
    }

    public static Orcamento Criar(Guid ordemServicoId, IReadOnlyCollection<Servico> servicos, DateTime? dataGeracao = null)
    {
        return new Orcamento(ordemServicoId, servicos, dataGeracao ?? DateTime.UtcNow);
    }

    public void ValidarRegeracao()
    {
        ValidarStatusGerado("Somente orcamento gerado pode ser gerado novamente.");
    }

    public void Regerar(IReadOnlyCollection<Servico> servicos, DateTime? dataGeracao = null)
    {
        ValidarRegeracao();
        DefinirServicos(servicos, dataGeracao ?? DateTime.UtcNow);
    }

    public void ValidarAprovacao()
    {
        ValidarStatusGerado("Somente orcamento gerado pode ser aprovado.");
    }

    public void Aprovar()
    {
        ValidarAprovacao();
        Status = StatusOrcamento.Aprovado;
    }

    public void ValidarRecusa()
    {
        ValidarStatusGerado("Somente orcamento gerado pode ser recusado.");
    }

    public void Recusar()
    {
        ValidarRecusa();
        Status = StatusOrcamento.Recusado;
    }

    private void ValidarStatusGerado(string mensagem)
    {
        if (Status != StatusOrcamento.Gerado)
        {
            throw new DomainException(mensagem);
        }
    }

    private void DefinirServicos(IReadOnlyCollection<Servico> servicos, DateTime dataGeracao)
    {
        if (dataGeracao == default)
        {
            throw new DomainException("A data de geracao do orcamento e obrigatoria.");
        }

        if (servicos is null || servicos.Count == 0)
        {
            throw new DomainException("A ordem de servico deve possuir ao menos um servico para gerar orcamento.");
        }

        DataGeracao = dataGeracao;
        _servicos.Clear();

        var servicosIdsAdicionados = new HashSet<Guid>();
        _servicos.AddRange(servicos
            .Where(servico => servicosIdsAdicionados.Add(servico.Id))
            .Select(OrcamentoServico.Criar));

        ValorTotal = decimal.Round(_servicos.Sum(item => item.ValorTotal), 2, MidpointRounding.ToEven);
    }
}
