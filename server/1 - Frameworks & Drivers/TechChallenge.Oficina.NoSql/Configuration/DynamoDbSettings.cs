namespace TechChallenge.Oficina.NoSql.Configuration;

public sealed class DynamoDbSettings
{
    public const string SectionName = "DynamoDbSettings";

    // Tabela do historico de movimentacoes de estoque (criada pelo Terraform na AWS; no ambiente local, pela propria API)
    public string TabelaHistoricoEstoque { get; set; } = string.Empty;

    // Endpoint do DynamoDB Local. Vazio na AWS: regiao e credenciais vem do ambiente.
    public string ServiceUrl { get; set; } = string.Empty;
}
