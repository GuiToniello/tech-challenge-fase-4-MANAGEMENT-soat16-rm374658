namespace TechChallenge.Oficina.Messaging.Configuration;

public sealed class SqsSettings
{
    public const string SectionName = "SqsSettings";

    // Fila FIFO consumida pelo Management (eventos publicados pelo OS Service e pelo Billing); vazia desliga o consumidor
    public string FilaConsumo { get; set; } = string.Empty;

    // Filas FIFO de destino dos eventos publicados pelo Management (OS Service e Billing): o mesmo envelope vai para cada fila, na ordem da lista
    public List<string> FilasPublicacao { get; set; } = [];

    // Endpoint alternativo (ex.: ElasticMQ local); vazio usa o endpoint da AWS da regiao configurada
    public string ServiceUrl { get; set; } = string.Empty;
}
