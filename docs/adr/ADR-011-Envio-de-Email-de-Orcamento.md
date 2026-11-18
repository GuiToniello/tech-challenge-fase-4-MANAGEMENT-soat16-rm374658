# ADR-011: Envio de E-mail de Orçamento com Resend

**Status**: Aceita (atualizada na fase 4: o e-mail de mudança de status passou a ser disparado pelo evento `OrdemServicoStatusAlterado`, ver [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md); depois, com a entrada do Billing ([ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md)), o orçamento e o e-mail dele saíram da Management, que ficou só com o e-mail de mudança de status)

**Contexto**:
Necessidade de notificar o cliente por e-mail quando um orçamento de ordem de serviço estiver disponível, mantendo separação de camadas e baixo acoplamento com provedor externo.

Atualmente o fluxo exige:
- Uso de uma porta de aplicação para envio de e-mail, sem dependência da aplicação para SDKs externos.
- Integração com provedor de e-mail transacional.
- Validação de configurações críticas (`ApiKey` e remetente) no momento do envio.
- Testabilidade da integração sem acoplar testes ao cliente real do provedor.

**Decisão**:
- Cada e-mail tem um contrato próprio no projeto **Application (UseCases)**, como porta de saída da aplicação, e a implementação concreta no projeto **Email**, que monta assunto e corpo HTML.
- O provedor escolhido é o **Resend**, encapsulado por um adapter (`ResendClientAdapter`) por meio da interface interna `IResendClient`, isolando o SDK externo do restante da aplicação.
- O registro de DI em `AddInfraEmail(...)` centraliza a inicialização dos serviços de e-mail e permite subir a aplicação sem credenciais válidas de envio (modo degradado para essa funcionalidade).
- A validação de `ResendSettings.ApiKey` e `ResendSettings.FromEmail` ocorre em runtime, apenas quando o envio é solicitado.
- A API carrega `ResendSettings` via configuração (`appsettings`/environment variables) e inicializa o módulo de e-mail no startup.

**E-mail do orçamento (hoje no Billing)**:
- Na versão original, a Management enviava o orçamento pela porta `IOrcamentoEmailSender` (implementada pelo `OrcamentoEmailSender`), com o id da OS, os serviços e os valores. O caso de uso exigia orçamento gerado, OS conhecida pela Management, cliente existente e e-mail do cliente preenchido.
- Com a entrada do Billing, esse envio saiu da Management junto com o orçamento. O Billing segue o mesmo desenho (porta na UseCases e Resend na infraestrutura) e recebe o e-mail do cliente no evento `OrdemServicoValidada` (ADR-020), sem ler o banco da Management. Detalhes na [ADR-021](https://github.com/GuiToniello/tech-challenge-fase-4-BILLING-soat16-rm374658/blob/main/docs/adr/ADR-021-Billing-Service-Orcamento-e-Pagamentos-com-Banco-Proprio.md).

**E-mail de mudança de status (fase 4)**:
- A OS pertence ao OS Service ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)). Por isso, a porta `IOrdemServicoStatusEmailSender` recebe um DTO, `NotificacaoStatusOrdemServico` (ids, status novo e nomes dos serviços), em vez da entidade da OS. Desde a entrada do Billing, o e-mail não mostra mais o valor do orçamento.
- O envio é disparado quando a Management consome o evento `OrdemServicoStatusAlterado` (`OrdemServicoReferenciaUseCases.AtualizarStatusAsync`), nos status `EmDiagnostico`, `EmExecucao`, `Finalizada` e `Entregue`. É assíncrono em relação à mudança de status, que acontece antes, no OS Service.
- Modo degradado: uma falha no envio, inclusive por timeout, só gera log (`integracao.falha` com `Operacao=notificacao_status`) e não desfaz a mudança de status.
- Vem desligado (`ResendSettings:SendEmailOnStatusChange=false`); para ligar, defina `ResendSettings__SendEmailOnStatusChange=true`, além da `ApiKey`. Sem `ApiKey` ou `FromEmail`, o e-mail simplesmente não sai, sem erro.

**Consequências**:
- ✅ Mantém arquitetura em camadas: aplicação define a porta, infraestrutura implementa.
- ✅ Facilita troca futura de provedor (SendGrid, SES etc.) sem alterar regras de negócio dos projetos Application/Entities.
- ✅ Melhora testabilidade com mocks de `IOrdemServicoStatusEmailSender` e `IResendClient`.
- ✅ Permite executar a API sem bloquear o startup quando a funcionalidade de e-mail não estiver configurada.
- ⚠ Em caso de configuração ausente/inválida, o problema só aparece em runtime: sem chave, o e-mail não sai; com uma chave inválida, sai o `integracao.falha`.
- ⚠ O corpo do e-mail está atualmente montado em string HTML na infraestrutura, exigindo cuidado de manutenção/layout.
- ⚠ O e-mail de status, disparado pelo evento, não é reenviado em caso de falha. O envio do orçamento, síncrono (a rota responde 500 se o Resend falhar), fica no Billing.

**Alternativas Rejeitadas**:
- Chamar o SDK do Resend diretamente no projeto Application (UseCases):
  - rejeitada por acoplamento indevido da camada de aplicação com tecnologia externa.
- Colocar lógica de envio no projeto Entities:
  - rejeitada por violar responsabilidade da camada de domínio (integração externa).
- Colocar o contrato em Entities:
  - rejeitada no estado atual por manter o contrato próximo ao caso de uso que o consome e por simplificar evolução dos fluxos de aplicação.
- Envio assíncrono via fila já nesta fase:
  - rejeitada por aumento de complexidade operacional para o escopo atual; pode ser evolução futura.
  - **Adotada em parte na fase 4** ([ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)): as filas SQS entraram para a comunicação entre os serviços, e o e-mail de status passou a sair do consumidor do evento. O envio do orçamento continua síncrono, agora no Billing.
