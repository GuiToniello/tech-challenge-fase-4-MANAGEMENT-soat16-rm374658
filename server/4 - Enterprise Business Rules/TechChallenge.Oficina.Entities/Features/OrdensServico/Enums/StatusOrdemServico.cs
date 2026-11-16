namespace TechChallenge.Oficina.Entities.Features.OrdensServico.Enums;

// Visao do Management sobre o status da OS, cujo ciclo de vida pertence ao OS Service: os valores sao o contrato dos eventos (statusAnterior/statusNovo).
public enum StatusOrdemServico
{
    Recebida = 1,
    EmDiagnostico = 2,
    AguardandoAprovacao = 3,
    EmExecucao = 4,
    Finalizada = 5,
    Entregue = 6,
    Encerrada = 7,
    Cancelada = 8
}
