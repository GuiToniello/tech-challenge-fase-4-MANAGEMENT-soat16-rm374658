namespace TechChallenge.Oficina.UseCases.Features.Eventos.Contratos;

// Servico precificado do evento OrdemServicoValidada.
public sealed record ServicoValidado(Guid ServicoId, string Nome, decimal Valor);
