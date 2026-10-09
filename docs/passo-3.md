# Passo 3 — Lançamento pela tela

Concluído em 09/10/2026. Contrato vigente: docs/passo-4.md.

> Contrato do passo atual. A IA lê só isto, o `CLAUDE.md` e o código. `docs/passo-1.md` e `docs/passo-2.md` são históricos e continuam valendo onde este não os altera.

## Objetivo e critério de pronto

**Frase do passo:** "Lanço uma operação pela página e ela aparece na carteira e na curva, sem mexer em CSV."

**Critério de pronto (todos obrigatórios):**

- [x] Página `/lancar` com formulário; ao salvar, a operação aparece em `/carteira` (tabela e curva) sem nenhum passo manual.
- [x] Uma operação lançada pela tela e depois reimportada por CSV conta como `jaExistentes`, nunca duplica (mesma `ChaveImportacao`).
- [x] Resgate maior que a posição ou operação após o vencimento são recusados com mensagem na própria página; nada é gravado.
- [x] A próxima aplicação real de vocês foi lançada pela tela, e o CSV não foi mais editado.
- [x] `/api/import` e a tela gravam pelo mesmo código (`Importacao`), e o e2e cobre os dois caminhos.
- [x] Core: testes 1–40 intactos e verdes. Nenhum teste novo no Core.

**Prazo:** 2 semanas a 1h/dia.

## Escopo

**Entra:**

- Extração da gravação do `/api/import` para uma classe `Importacao` em `Carteira.Web`, usada pelos dois escritores. É o item de polimento que esperava o segundo uso; o segundo uso chegou.
- Endpoint `POST /api/operacoes` (JSON com os mesmos campos do CSV) → monta uma linha CSV em memória → `CsvTradeParser.Parse` → `Importacao`. É o caminho da tela e do e2e.
- Página `/lancar`: formulário com os campos do CSV, titulares e ativos existentes em `select`, campos para ativo novo, mensagem de sucesso ou erro, e a lista das últimas 20 operações abaixo.
- Invalidação dos snapshots igual à do import (já está na `Importacao`).
- E2e: cenário que lança via `POST /api/operacoes` e confere idempotência contra o CSV.

**Fica de fora (não implementar nem "deixar preparado"):**

- Editar ou apagar operação. Corrigir erro continua sendo apagar no banco e reimportar, ou lançar o inverso.
- Criar titular pela tela (titulares novos continuam vindo pelo CSV; são dois e não mudam).
- Upload de CSV pela tela.
- Validação em JavaScript, máscara de campo, autocomplete.
- Login, CSRF além do padrão do Blazor, confirmação por e-mail.
- Qualquer mudança no Core.

## Mudanças no domínio

Nenhuma. `Titular`, `Ativo`, `Operacao`, `PrecoDiario` e `SnapshotDiario` não mudam. O formulário produz exatamente o que o CSV produz.

**Decisão central:** o formulário não tem validação própria. Ele monta a linha

`data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas`

com os valores digitados, no formato do contrato do Passo 1 (`dd/MM/yyyy`, vírgula decimal), e passa cabeçalho + linha para `CsvTradeParser.Parse(stream, hoje)`, com `hoje` vindo de `Relogio.HojeSaoPaulo()`, como no import e no job. Toda regra de formato, a regra "não futura" e a `ChaveImportacao` vêm do parser. Erro do parser vira mensagem na tela, sem o prefixo "linha 2".

## `Importacao` (Carteira.Web)

Classe nova, única mudança estrutural do passo. É um *move*: o corpo do `/api/import` sai de `Program.cs` para cá, sem mudar comportamento.

`Importacao.ExecutarAsync(IReadOnlyList<LinhaOperacao> linhas, CancellationToken ct)` → `ResultadoImportacao { Importadas, JaExistentes, AtivosCriados, TitularesCriados }` ou lança `ImportacaoInvalidaException(mensagem)` para resgate sem posição / após vencimento.

Faz, nesta ordem, o que o import já faz hoje: cria titulares e ativos que faltam, descarta chaves conhecidas, valida invariantes sobre existente + novo com `PositionCalculator.Calculate`, grava em transação, invalida snapshots com `date ≥` menor data nova e dispara `PreencherSnapshotsAsync`.

`/api/import` passa a ser: ler o arquivo → `Parse` → `Importacao.ExecutarAsync` → JSON. `POST /api/operacoes` é: ler o JSON → montar a linha → `Parse` → `Importacao.ExecutarAsync` → JSON. A página chama `Importacao` direto, sem passar por HTTP.

**Regras do bloco:** `DbContext` direto, sem interface, sem repository. Registrada como `Scoped`. Recebe `PriceSyncJob` para o preenchimento, como o endpoint recebe hoje. Usa `Relogio.HojeSaoPaulo()` onde precisar de data de hoje.

## `POST /api/operacoes`

Corpo JSON, campos com os nomes das colunas do CSV:

```json
{ "data": "10/01/2025", "titular": "renato", "codigo": "tesouro-selic-2029-03-01",
  "titulo": "Tesouro Selic 2029", "vencimento": "01/03/2029", "tipo": "APLICACAO",
  "quantidade": "2,5", "preco_unitario": "14000,00", "taxas": "0" }
```

Todos string, exatamente como viriam no CSV; `vencimento` e `taxas` podem vir vazios com as mesmas regras do CSV. Resposta: o mesmo JSON do `/api/import`. Erro do parser ou da `Importacao` → `400` com `{ "erro": "..." }`.

## Página `/lancar`

Blazor Server, `EditForm`, sem componente reutilizável, sem CSS além do padrão. Campos:

| Campo | Controle | Regra |
| --- | --- | --- |
| Titular | `select` dos titulares do banco | obrigatório |
| Título | `select` dos ativos do banco + opção "outro" | obrigatório |
| Código, nome, vencimento | textos; só habilitados com "outro" | preenchem `codigo`, `titulo`, `vencimento` |
| Data | `input type="date"` | convertido para `dd/MM/yyyy` |
| Tipo | `select` APLICACAO / RESGATE | obrigatório |
| Quantidade, preço, taxas | textos | enviados como digitados; o parser valida |

Ao salvar: monta a linha, `Parse`, `Importacao`. Sucesso → mensagem "Operação lançada" e o formulário limpa; a lista abaixo atualiza. Erro → a mensagem do parser ou da `Importacao`, o formulário mantém os valores.

**Lista:** as últimas 20 operações por data decrescente: data, titular, título, tipo, quantidade, preço. Só leitura. Link para `/carteira` no topo e, em `/carteira`, um link para `/lancar`.

## Casos de teste

Nenhum novo no Core. O Core não muda; se a implementação precisar de algo no Core, é sinal de que saiu do desenho — pare e revise.

**E2e — cenário novo em `run.sh`, após o fluxo atual:**

1. `POST /api/operacoes` com uma linha que **não** está em `operacoes.csv` (ex.: Selic 2029, renato, 1,0 a 15.000,00 em 01/08/2025) → `importadas=1`.
2. `GET /carteira` reflete a nova quantidade (Selic 3,0 em vez de 2,0) na tabela e no "Valor hoje".
3. `POST /api/import` com um CSV que contém essa mesma linha → `jaExistentes` inclui ela, `importadas=0`.
4. `POST /api/operacoes` com resgate de 100 títulos → `400` com `erro` contendo "posição"; a tabela não muda.
5. `POST /api/operacoes` com data de amanhã → `400`.

## Tarefas, modelos e prazo

- [x] **T0 — Pré-requisito.** Passo 2 fechado: T7 verde, dois dias de observação, critério de pronto marcado, commit "Passo 2 concluído". Sem isso, nada abaixo começa.
- [x] **T1 — `Importacao`.** Mover o corpo do `/api/import` para a classe; o endpoint fica com poucas linhas. É refatoração protegida pelo e2e: mesma entrada, mesma saída. Critério: e2e verde, `git diff --stat -- src/Carteira.Core tests/Carteira.Core.Tests` vazio. (1 sessão)
- [x] **T2 — `POST /api/operacoes`.** Montagem da linha + `Parse` + `Importacao`. Critério: os cenários 1, 3, 4 e 5 do e2e passam via `curl` pelo túnel, contra a VPS. (1 sessão)
- [x] **T3 — Página `/lancar`.** Formulário + lista + links. Critério: lançar uma operação de teste pelo navegador e vê-la na carteira e na curva; depois apagá-la no banco e reconstruir os snapshots. (2 sessões)
- [x] **T4 — E2e.** Os 5 cenários no `run.sh`. (1 sessão)
- [x] **T5 — Uso real.** A próxima aplicação de vocês entra pela tela. Marcar o critério de pronto. (quando acontecer)

Soma: ~5 sessões. Prazo de 2 semanas tem folga.

**Modelo por tarefa:** Sonnet 5 em todas. Não há número que precise sair exato de um documento; é casca.

**Lista de "ideias para depois"** (preencher, não executar):

- Editar e apagar operação pela tela.
- Criar titular pela tela, com nome diferente do slug.
- Upload do CSV pela tela.
- Passo 4: linha do CDI na curva (tabela de índices com coluna `indice`; fonte SGS/BCB, série 12).
- Índices além do CDI: IPCA e poupança (mensais, exigem regra de periodicidade); Ibovespa só com ações.