using Amazon.SQS;
using Amazon.SQS.Model;

namespace TechChallenge.Oficina.Messaging.Sqs;

public sealed class SqsClientAdapter : ISqsClient
{
    private const string AtributoTipo = "tipo";

    private readonly IAmazonSQS _sqs;

    public SqsClientAdapter(IAmazonSQS sqs)
    {
        _sqs = sqs;
    }

    public async Task<string> GetQueueUrlAsync(string queueName, CancellationToken cancellationToken = default)
    {
        var response = await _sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, cancellationToken);
        return response.QueueUrl;
    }

    public async Task SendMessageAsync(string queueUrl, string body, string tipo, string messageGroupId, string messageDeduplicationId, CancellationToken cancellationToken = default)
    {
        await _sqs.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = queueUrl,
            MessageBody = body,
            MessageGroupId = messageGroupId,
            MessageDeduplicationId = messageDeduplicationId,
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                [AtributoTipo] = new() { DataType = "String", StringValue = tipo }
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<MensagemSqs>> ReceiveMessagesAsync(string queueUrl, int maxNumberOfMessages, int waitTimeSeconds, CancellationToken cancellationToken = default)
    {
        var response = await _sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = queueUrl,
            MaxNumberOfMessages = maxNumberOfMessages,
            WaitTimeSeconds = waitTimeSeconds,
            MessageAttributeNames = [AtributoTipo]
        }, cancellationToken);

        // No AWSSDK v4 as colecoes da resposta ficam nulas quando nao ha mensagens
        return response.Messages?
            .Select(mensagem => new MensagemSqs(mensagem.MessageId, mensagem.ReceiptHandle, mensagem.Body))
            .ToArray() ?? [];
    }

    public async Task DeleteMessageAsync(string queueUrl, string receiptHandle, CancellationToken cancellationToken = default)
    {
        await _sqs.DeleteMessageAsync(new DeleteMessageRequest { QueueUrl = queueUrl, ReceiptHandle = receiptHandle }, cancellationToken);
    }
}
