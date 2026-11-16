namespace TechChallenge.Oficina.Messaging.Configuration;

public sealed class SqsSettings
{
    public const string SectionName = "SqsSettings";

    // Fila FIFO consumida pelo Management (eventos publicados pelo OS Service); vazia desliga o consumidor
    public string FilaConsumo { get; set; } = string.Empty;

    // Fila FIFO consumida pelo OS Service (eventos publicados pelo Management)
    public string FilaPublicacao { get; set; } = string.Empty;

    // Endpoint alternativo (ex.: ElasticMQ local); vazio usa o endpoint da AWS da regiao configurada
    public string ServiceUrl { get; set; } = string.Empty;
}
