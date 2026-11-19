namespace TechChallenge.Oficina.Messaging.Features.Eventos;

// Envelope JSON (camelCase) trocado com o OS Service, o Billing e o Execucao e Producao: {"id","tipo","versao","ocorridoEm","origem","dados"}.
public sealed record EnvelopeEvento<TDados>(Guid Id, string Tipo, int Versao, DateTime OcorridoEm, string Origem, TDados Dados);
