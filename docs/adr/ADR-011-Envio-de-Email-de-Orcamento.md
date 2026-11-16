# ADR-011: Envio de E-mail de Orçamento com Resend

**Status**: Aceita (atualizada na fase 4: o orçamento virou um agregado próprio da Management, e o e-mail de mudança de status passou a ser disparado pelo evento `OrdemServicoStatusAlterado`, ver [ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md))

**Contexto**:
Necessidade de notificar o cliente por e-mail quando um orçamento de ordem de serviço estiver disponível, mantendo separação de camadas e baixo acoplamento com provedor externo.

Atualmente o fluxo exige:
- Uso de uma porta de aplicação para envio de e-mail (`IOrcamentoEmailSender`), sem dependência da aplicação para SDKs externos.
- Integração com provedor de e-mail transacional.
- Validação de configurações críticas (`ApiKey` e remetente) no momento do envio.
- Testabilidade da integração sem acoplar testes ao cliente real do provedor.

**Decisão**:
- O contrato de envio de orçamento por e-mail fica no projeto **Application (UseCases)** (`IOrcamentoEmailSender`, em `Features/Orcamentos/UseCases`), como porta de saída da aplicação. Ele recebe o agregado `Orcamento`, cujo id é o da OS, e o e-mail de destino.
- O caso de uso no projeto **Application (UseCases)** (`OrcamentoUseCases.EnviarPorEmailAsync`, rota `POST /api/orcamentos/{ordemServicoId}/enviar`) orquestra regras de negócio e dispara a porta de saída:
  - exige orçamento gerado;
  - exige a OS conhecida pela Management (a referência recebida por evento do OS Service, ADR-020), de onde vem o cliente;
  - exige cliente existente;
  - exige e-mail do cliente preenchido.
- A implementação concreta fica no projeto **Email** (`OrcamentoEmailSender`) e monta assunto/corpo HTML com os dados do orçamento (id da OS, serviços e valores).
- O provedor escolhido é o **Resend**, encapsulado por um adapter (`ResendClientAdapter`) por meio da interface interna `IResendClient`, isolando o SDK externo do restante da aplicação.
- O registro de DI em `AddInfraEmail(...)` centraliza a inicialização dos serviços de e-mail e permite subir a aplicação sem credenciais válidas de envio (modo degradado para essa funcionalidade).
- A validação de `ResendSettings.ApiKey` e `ResendSettings.FromEmail` ocorre em runtime no `OrcamentoEmailSender`, apenas quando o envio é solicitado.
- A API carrega `ResendSettings` via configuração (`appsettings`/environment variables) e inicializa o módulo de e-mail no startup.

**E-mail de mudança de status (fase 4)**:
- A OS pertence ao OS Service ([ADR-019](https://github.com/GuiToniello/tech-challenge-fase-4-OS-SERVICE-soat16-rm374658/blob/main/docs/adr/ADR-019-OS-Service-Microsservico-com-Banco-Proprio.md)). Por isso, a porta `IOrdemServicoStatusEmailSender` recebe um DTO, `NotificacaoStatusOrdemServico` (ids, status novo, valor do orçamento e nomes dos serviços), em vez da entidade da OS.
- O envio é disparado quando a Management consome o evento `OrdemServicoStatusAlterado` (`OrdemServicoReferenciaUseCases.AtualizarStatusAsync`), nos status `EmDiagnostico`, `EmExecucao`, `Finalizada` e `Entregue`. É assíncrono em relação à mudança de status, que acontece antes, no OS Service.
- Modo degradado: uma falha no envio, inclusive por timeout, só gera log (`integracao.falha` com `Operacao=notificacao_status`) e não desfaz a mudança de status.
- Vem desligado (`ResendSettings:SendEmailOnStatusChange=false`); para ligar, defina `ResendSettings__SendEmailOnStatusChange=true`, além da `ApiKey`.

**Consequências**:
- ✅ Mantém arquitetura em camadas: aplicação define a porta, infraestrutura implementa.
- ✅ Facilita troca futura de provedor (SendGrid, SES etc.) sem alterar regras de negócio dos projetos Application/Entities.
- ✅ Melhora testabilidade com mocks de `IOrcamentoEmailSender` e `IResendClient`.
- ✅ Permite executar a API sem bloquear o startup quando a funcionalidade de e-mail não estiver configurada.
- ⚠ Em caso de configuração ausente/inválida, a falha aparece em runtime ao tentar enviar orçamento por e-mail.
- ⚠ O corpo do e-mail está atualmente montado em string HTML na infraestrutura, exigindo cuidado de manutenção/layout.
- ⚠ Falhas de provedor externo podem impactar o envio do orçamento, que continua síncrono (a rota responde 500, sem nova tentativa). O e-mail de status, disparado pelo evento, também não é reenviado em caso de falha.

**Alternativas Rejeitadas**:
- Chamar o SDK do Resend diretamente no projeto Application (UseCases):
  - rejeitada por acoplamento indevido da camada de aplicação com tecnologia externa.
- Colocar lógica de envio no projeto Entities:
  - rejeitada por violar responsabilidade da camada de domínio (integração externa).
- Colocar o contrato em Entities:
  - rejeitada no estado atual por manter o contrato próximo ao caso de uso que o consome e por simplificar evolução dos fluxos de aplicação.
- Envio assíncrono via fila já nesta fase:
  - rejeitada por aumento de complexidade operacional para o escopo atual; pode ser evolução futura.
  - **Adotada em parte na fase 4** ([ADR-020](ADR-020-Eventos-com-SQS-e-Saga-Coreografada.md)): as filas SQS entraram para a comunicação entre os serviços, e o e-mail de status passou a sair do consumidor do evento. O envio do orçamento continua síncrono.
