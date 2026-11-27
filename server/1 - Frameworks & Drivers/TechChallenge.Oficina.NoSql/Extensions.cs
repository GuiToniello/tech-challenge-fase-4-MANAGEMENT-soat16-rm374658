using Amazon.DynamoDBv2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TechChallenge.Oficina.NoSql.Configuration;
using TechChallenge.Oficina.NoSql.DynamoDb;
using TechChallenge.Oficina.NoSql.Features.Insumos;
using TechChallenge.Oficina.UseCases.Features.Insumos.Interfaces;

namespace TechChallenge.Oficina.NoSql;

public static class Extensions
{
    public static IServiceCollection AddInfraNoSql(this IServiceCollection services, DynamoDbSettings dynamoDbSettings)
    {
        services.AddSingleton(dynamoDbSettings);
        services.AddSingleton<IAmazonDynamoDB>(_ => CriarClienteDynamoDb(dynamoDbSettings));
        services.AddSingleton<IDynamoDbClient, DynamoDbClientAdapter>();
        services.AddSingleton<IHistoricoEstoqueGateway, HistoricoEstoqueGateway>();

        return services;
    }

    // So no ambiente local (ServiceUrl preenchido): cria a tabela do historico de estoque no DynamoDB Local com as mesmas chaves e TTL da AWS.
    // Na AWS a tabela e do Terraform e o IAM da Management so permite PutItem e Query. Uma falha aqui nao impede a API de subir.
    public static async Task GarantirTabelasLocaisAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        var settings = serviceProvider.GetRequiredService<DynamoDbSettings>();

        if (string.IsNullOrWhiteSpace(settings.ServiceUrl))
        {
            return;
        }

        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Extensions));

        try
        {
            var criada = await serviceProvider.GetRequiredService<IDynamoDbClient>().CriarTabelaSeNaoExisteAsync(
                settings.TabelaHistoricoEstoque,
                HistoricoEstoqueGateway.ChaveParticao,
                HistoricoEstoqueGateway.ChaveOrdenacao,
                HistoricoEstoqueGateway.AtributoExpiracao,
                cancellationToken);

            logger.LogInformation(
                "Tabela {Tabela} do DynamoDB local {Situacao} em {ServiceUrl}.",
                settings.TabelaHistoricoEstoque,
                criada ? "criada" : "ja existente",
                settings.ServiceUrl);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                exception,
                "Falha ao criar a tabela {Tabela} no DynamoDB local em {ServiceUrl}; o historico de estoque fica indisponivel, mas a API segue.",
                settings.TabelaHistoricoEstoque,
                settings.ServiceUrl);
        }
    }

    private static AmazonDynamoDBClient CriarClienteDynamoDb(DynamoDbSettings dynamoDbSettings)
    {
        // Local: ServiceUrl aponta para o DynamoDB Local (credenciais ficticias via AWS_ACCESS_KEY_ID/AWS_SECRET_ACCESS_KEY).
        // Na AWS fica vazio: regiao (AWS_REGION) e credenciais vem do ambiente do pod.
        if (!string.IsNullOrWhiteSpace(dynamoDbSettings.ServiceUrl))
        {
            return new AmazonDynamoDBClient(new AmazonDynamoDBConfig { ServiceURL = dynamoDbSettings.ServiceUrl });
        }

        return new AmazonDynamoDBClient();
    }
}
