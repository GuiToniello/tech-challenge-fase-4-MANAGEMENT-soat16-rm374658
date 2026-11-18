using TechChallenge.Oficina.Entities.Exceptions;
using TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;

namespace TechChallenge.Oficina.Entities.Features.OrdensServico;

// Referencia local (read model) da ordem de servico: o ciclo de vida da OS pertence ao OS Service e esta copia e mantida pelos eventos recebidos via SQS.
public sealed class OrdemServicoReferencia
{
    private List<Guid> _servicoIds = [];

    public Guid Id { get; private set; }
    public Guid ClienteId { get; private set; }
    public Guid VeiculoId { get; private set; }
    public IReadOnlyCollection<Guid> ServicoIds => _servicoIds.AsReadOnly();
    public StatusOrdemServico Status { get; private set; }

    // Indica que o estoque dos insumos ja foi debitado pelo pagamento aprovado (Billing): garante um unico debito por OS
    public bool EstoqueDebitado { get; private set; }

    private OrdemServicoReferencia()
    {
    }

    private OrdemServicoReferencia(Guid ordemServicoId, Guid clienteId, Guid veiculoId, IReadOnlyCollection<Guid> servicoIds)
    {
        if (ordemServicoId == Guid.Empty)
        {
            throw new DomainException("O identificador da ordem de servico e obrigatorio.");
        }

        Id = ordemServicoId;
        Atualizar(clienteId, veiculoId, servicoIds);
        Status = StatusOrdemServico.Recebida;
    }

    public static OrdemServicoReferencia Criar(Guid ordemServicoId, Guid clienteId, Guid veiculoId, IReadOnlyCollection<Guid> servicoIds)
    {
        return new OrdemServicoReferencia(ordemServicoId, clienteId, veiculoId, servicoIds);
    }

    public void Atualizar(Guid clienteId, Guid veiculoId, IReadOnlyCollection<Guid> servicoIds)
    {
        if (clienteId == Guid.Empty)
        {
            throw new DomainException("O cliente da ordem de servico e obrigatorio.");
        }

        if (veiculoId == Guid.Empty)
        {
            throw new DomainException("O veiculo da ordem de servico e obrigatorio.");
        }

        if (servicoIds is null || servicoIds.Count == 0)
        {
            throw new DomainException("A ordem de servico deve possuir ao menos um servico.");
        }

        if (servicoIds.Any(servicoId => servicoId == Guid.Empty))
        {
            throw new DomainException("Todos os servicos informados devem possuir identificador valido.");
        }

        ClienteId = clienteId;
        VeiculoId = veiculoId;
        _servicoIds = servicoIds.Distinct().ToList();
    }

    public void AtualizarStatus(StatusOrdemServico status)
    {
        if (!Enum.IsDefined(typeof(StatusOrdemServico), status))
        {
            throw new DomainException("O status da ordem de servico informado e invalido.");
        }

        Status = status;
    }

    public void RegistrarDebitoEstoque()
    {
        if (EstoqueDebitado)
        {
            throw new DomainException("O estoque da ordem de servico ja foi debitado.");
        }

        EstoqueDebitado = true;
    }
}
