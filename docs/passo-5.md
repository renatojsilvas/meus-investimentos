# Passo 5 — Renda fixa com PU manual

> Contrato do passo atual. A IA lê só isto, o `CLAUDE.md` e o código. `docs/passo-1.md` a `docs/passo-4.md` são históricos e continuam valendo onde este não os altera.

## Objetivo e critério de pronto

**Frase do passo:** "Lanço meus CDBs, LCIs e LCAs como ativos de renda fixa, atualizo o PU deles pela tela quando a corretora mostra, e a carteira e a curva passam a incluir tudo."

**Critério de pronto (todos obrigatórios):**

- [ ] Todos os títulos de renda fixa bancária de vocês estão no banco, com classe `RendaFixa`, lançados por CSV ou pela tela.
- [ ] A página `/carteira` mostra cada um com quantidade, PU, valor e rentabilidade, dentro do bloco do titular, e o total geral inclui a renda fixa.
- [ ] Atualizei o PU de um deles pela tela; a tabela e a curva refletiram no mesmo dia, sem passo manual.
- [ ] O job diário e os backfills de preço ignoram `RendaFixa` (nenhuma chamada à API do Tesouro com código inventado; log limpo).
- [ ] Um ativo vencido com posição aparece destacado na página com "venceu em dd/mm/aaaa — lance o resgate".
- [ ] O CSV atual (sem a coluna `classe`) continua importando igual: `jaExistentes` cobre tudo, `importadas=0`.
- [ ] Core: testes 1–50 verdes. E2e verde com o cenário de renda fixa.

**Prazo:** 1 semana a 1h/dia. Passou, o escopo estava errado: cortar, não esticar.

## Escopo

**Entra:**

- `ClasseAtivo.RendaFixa`. Nenhum campo novo em `Ativo`: `Codigo` é um slug inventado pelo usuário, `Vencimento` continua obrigatório.
- Coluna `classe` no CSV, **opcional e última**: ausente ou vazia = `TESOURO`; `RENDA_FIXA` para a classe nova. O CSV atual importa sem mudança.
- Formulário "Atualizar PU" em `/lancar`: escolhe o ativo (qualquer classe), digita o PU e a data (default hoje); grava `PrecoDiario(ativo, data, pu)`, substituindo se já existir; dispara a reconstrução dos snapshots a partir dessa data.
- Endpoint `POST /api/precos` com o mesmo contrato, para o e2e e para `curl`.
- Job diário, `BackfillPrecosAsync` e `DiaEstaCompleto` consideram só ativos `TesouroDireto`.
- Aviso de vencidos na página, para qualquer classe.
- "PU" no lugar de "Preço" nos cabeçalhos de tabela e rótulos de formulário.
- E2e: um CDB no CSV, `POST /api/precos`, página com o valor, aviso de vencido.

**Fica de fora (não implementar nem "deixar preparado"):**

- Marcação na curva automática (taxa contratada × dias úteis, % do CDI, IPCA+). Se um dia fizer falta, é passo próprio.
- Campos de taxa, indexador, emissor, liquidez. Só o que já existe em `Ativo`.
- Resgate automático no vencimento. O aviso existe; o lançamento é manual.
- Cupom, custódia, IR, IOF.
- Ações, FII, stocks, cripto (Passos 6–8).
- Qualquer mudança em R1–R13, `PositionCalculator`, `DailySeries`, `BenchmarkSeries`.

## Mudanças no domínio

Nenhuma entidade nova. Uma mudança de enum e uma convenção.

**`ClasseAtivo`** ganha `RendaFixa`. A classe não altera nenhuma regra de cálculo: R1–R6 valem igual, porque tudo é `Quantidade × PU`. O que a classe decide é **de onde vem o preço**: `TesouroDireto` da API; `RendaFixa` da tela.

**Convenção de unidade para `RendaFixa`:** quantidade real de unidades e PU da unidade, como a corretora mostra (ex.: 10 unidades de CDB a PU 1.000,00 na emissão; hoje PU 1.047,32). Quem preferir lançar pelo valor usa quantidade 1 e PU = valor total — o modelo não distingue.

**`Codigo` de um ativo `RendaFixa`:** slug inventado pelo usuário, mesma regra `^[a-z0-9-]+$`, único. Sugestão de forma: `cdb-<emissor>-<aaaa-mm>` (ex.: `cdb-inter-2027-03`). **É chave de `ChaveImportacao`: não renomear depois de importado.**

**Invariantes (já existentes, agora valendo para a classe nova):**

- Operação após o `Vencimento` → `OperacaoAposVencimentoException`. Vale para o resgate no vencimento: a data do resgate é o próprio dia do vencimento, não depois.
- Preço duplicado para (ativo, data) → substitui.

**Banco:** sem migration de tabela. A coluna `assets.class` já existe (enum como string); só passa a aceitar o valor novo.

## Regras

Nenhuma regra numerada nova. Três esclarecimentos sobre as existentes:

**R4 para `RendaFixa`:** o PU na data D é o mais recente `≤ D` gravado pela tela. Entre duas atualizações o valor fica constante; a curva mostra degraus. É a informação que existe, sem interpolação.

**R12 (série CDI) para `RendaFixa`:** nada muda — fluxos de caixa são fluxos de caixa.

**Aviso de vencidos (Web, não Core):** ativo com `Vencimento < hoje` e alguma posição com `Quantidade > 0` em `hoje`. A página destaca a linha e escreve "venceu em dd/mm/aaaa — lance o resgate". Não altera totais.

**Exemplo de referência (vira o teste 49)** — titular `renato`, `cdb-inter-2027-03`, vencimento 01/03/2027:

| Data | Operação | Qtd | PU (R$) | Qtd após | Custo após (R$) |
| --- | --- | --- | --- | --- | --- |
| 10/01/2025 | Aplicação | 10 | 1.000,00 | 10 | 10.000,00 |

Preços pela tela: 30/06/2025 PU 1.047,32; 31/12/2025 PU 1.101,50.

- Em 15/09/2025 (entre as duas atualizações): valor = 10 × 1.047,32 = **10.473,20**; rentabilidade **473,20** (4,73%).
- Em 15/01/2026: valor = **11.015,00**; rentabilidade **1.015,00** (10,15%).
- Em 05/01/2025 (antes de tudo): sem posição; nada.

## Importação e tela

**CSV:** cabeçalho passa a aceitar 9 ou 10 colunas. Com 10, a última é `classe`:

```csv
data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas;classe
10/01/2025;renato;cdb-inter-2027-03;CDB Inter 2027;01/03/2027;APLICACAO;10;1000,00;0;RENDA_FIXA
10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0;
```

Valores aceitos: `TESOURO`, `RENDA_FIXA`, vazio (= `TESOURO`), sem distinção de caixa. Outro valor é erro da linha. A classe entra na `LinhaOperacao`; a `Importacao` usa ao criar o ativo. **Ativo que já existe com outra classe → erro da linha** ("classe diverge do cadastro").

**`POST /api/operacoes`** ganha o campo opcional `classe`, mesmas regras.

**`/lancar`:** o select de título ganha a classe entre parênteses; "outro" ganha um select de classe (default Tesouro). Abaixo do formulário de operação, um segundo bloco **"Atualizar PU"**: select de ativo (todos), campo PU, campo data (default hoje, não futura), botão. Sucesso: "PU gravado" e a lista de últimos preços manuais (10) atualiza. Erro: mensagem.

**`POST /api/precos`:** JSON `{ "codigo": "cdb-inter-2027-03", "data": "30/06/2025", "pu": "1047,32" }`, todos string no formato do CSV. Valida: código existe; data `dd/MM/yyyy` não futura; PU > 0. Grava substituindo; chama `ReconstruirSnapshotsAsync(data)`. Resposta `{ "codigo", "data", "pu" }`; erro → `400 { "erro" }`.

**Job e backfills:** onde hoje se itera sobre ativos com posição, filtra `Classe == TesouroDireto`. `BackfillPrecosAsync` idem. Nenhuma chamada à API com código de renda fixa.

## Página

- Tabelas por titular e consolidado: cabeçalho "Preço" vira "PU". Linhas de `RendaFixa` aparecem junto das demais, sem bloco separado. Uma coluna "Classe" não entra; o nome do título basta.
- Linha vencida com posição: texto "venceu em dd/mm/aaaa — lance o resgate" na célula do título, em negrito. Sem CSS novo.
- Nada muda no gráfico: a renda fixa já entra no valor dos snapshots.

## Casos de teste

Lista fechada. Testes 1–48 não mudam.

| # | Caso | Resultado esperado |
| --- | --- | --- |
| 49 | Exemplo de referência: `Calculate` em 15/09/2025 e em 15/01/2026 com os dois preços manuais | 10.473,20 / 473,20 e 11.015,00 / 1.015,00 (2 casas); classe do ativo não altera nada |
| 50 | `CsvTradeParser`: linha com `classe` = `RENDA_FIXA`, linha com vazio, linha com `tesouro` minúsculo, linha com valor desconhecido, cabeçalho de 9 colunas | `RendaFixa`; `TesouroDireto`; `TesouroDireto`; erro da linha; aceito com todas `TesouroDireto` |

**Decisões de implementação** (preencher ao escrever os testes).

**Fora dos testes, de propósito:** aviso de vencidos (é filtro na Web), endpoint, formulário, filtro do job. Cobertos pelo e2e.

**E2e — cenário novo em `run.sh`, após o fluxo atual:**

- `operacoes.csv` ganha a 10ª coluna e uma linha de CDB (`cdb-teste-2027-03`, 10 a 1.000,00, vencimento 01/03/2027) e uma de CDB já vencido (`cdb-vencido-2025-06`, 5 a 1.000,00, vencimento 30/06/2025, sem resgate).
- Reimporta o CSV **antigo de 9 colunas** → `importadas=0` (compatibilidade).
- `POST /api/precos` com PU 1.047,32 para o CDB → `GET /carteira` contém 10.473,20 na linha do CDB e o total geral subiu exatamente 10.473,20 em relação ao cenário anterior; o backfill de preços continua devolvendo o mesmo `ativos` de antes (o CDB não entrou).
- A página contém "venceu em 30/06/2025".
- `POST /api/precos` com data de amanhã → `400`.

## Tarefas, modelos e prazo

- [ ] **T0 — Pré-requisito.** Passo 4 fechado: "Passo 4 concluído" commitado; polimento P10–P12 feito.
- [ ] **T1 — Lista real.** Levantar na corretora os títulos de renda fixa de vocês: código inventado, nome, vencimento, quantidade, PU de emissão, data de aplicação, PU atual. Planilha fora do repo. Sem código. (30 min)
- [ ] **T2 — Core.** `ClasseAtivo.RendaFixa`; parser lê a coluna opcional `classe`. Testes 49–50 antes (Sonnet serve: os números são triviais), implementação depois. Critério: 1–50 verdes. (1 sessão)
- [ ] **T3 — Importação, endpoint e job.** `Importacao` usa a classe; `POST /api/precos`; filtro `TesouroDireto` no job e no backfill. Critério: e2e atual verde; `curl` do `POST /api/precos` na VPS grava e a curva muda. (1 sessão)
- [ ] **T4 — Tela.** Classe no `/lancar`, bloco "Atualizar PU", "PU" nos cabeçalhos, aviso de vencidos. Critério: atualizar um PU real pelo navegador e ver na tabela. (1 sessão)
- [ ] **T5 — Carga real.** Importar a planilha da T1 (CSV de 10 colunas) e atualizar os PUs atuais pela tela. Critério: total geral = Tesouro + renda fixa, conferido com a corretora. (meia sessão)
- [ ] **T6 — E2e + observação.** Cenário novo verde; 2 dias vendo o job ignorar a renda fixa (log sem código inventado). Marcar o critério de pronto. (1 sessão + 2 dias)

Soma: ~5 sessões. Prazo de 1 semana tem folga.

**Modelo por tarefa:**

| Tarefa | Modelo | Por quê |
| --- | --- | --- |
| T1, T5 | nenhum | planilha e tela |
| T2 — testes e implementação | Sonnet 5 | dois testes com números simples; sem risco de erro de cópia |
| T3, T4, T6 | Sonnet 5 | casca bem especificada |

**Lista de "ideias para depois"** (preencher, não executar):

- Marcação na curva automática por taxa contratada (prefixado, % CDI, IPCA+), como alternativa ao PU manual.
- Resgate automático no vencimento pelo último PU.
- Histórico de PUs manuais na página, por ativo.
- 401/403 da API do Tesouro encerra o loop do job inteiro (herdado do Passo 4): em 401, pular só o Tesouro e seguir.