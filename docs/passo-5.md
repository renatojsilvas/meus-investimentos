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
- [ ] Um ativo sem vencimento (cofrinho) está no banco, aparece na página sem aviso de vencido, e aceita aplicação em qualquer data.
- [ ] Core: testes 1–52 verdes. E2e verde com o cenário de renda fixa.

**Prazo:** 10 dias a 1h/dia. Passou, o escopo estava errado: cortar, não esticar.

## Escopo

**Entra:**

- `ClasseAtivo.RendaFixa`. `Ativo.Vencimento` passa a ser **opcional** (`DateOnly?`): CDB/LCI têm; cofrinhos e contas remuneradas não têm. Ações, FII, stocks e cripto (Passos 6–8) também não terão — esta é a única concessão ao futuro deste passo. `Codigo` é um slug inventado pelo usuário.
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
- Qualquer mudança em R1–R6, R8, R10–R13, `BenchmarkSeries`. No `PositionCalculator`, só a invariante de vencimento passa a ser condicional. Em `DailySeries`, só a mudança de R7 descrita em "Regras" (descoberta na T4: PU manual de um titular criava dia sem ponto para o outro e o total da curva despencava).

## Mudanças no domínio

Nenhuma entidade nova. Uma mudança de enum, um campo que vira opcional e uma convenção.

**`ClasseAtivo`** ganha `RendaFixa`. A classe não altera nenhuma regra de cálculo: R1–R6 valem igual, porque tudo é `Quantidade × PU`. O que a classe decide é **de onde vem o preço**: `TesouroDireto` da API; `RendaFixa` da tela.

**Convenção de unidade para `RendaFixa`:** quantidade real de unidades e PU da unidade, como a corretora mostra (ex.: 10 unidades de CDB a PU 1.000,00 na emissão; hoje PU 1.047,32). Quem preferir lançar pelo valor usa quantidade 1 e PU = valor total — o modelo não distingue.

**`Codigo` de um ativo `RendaFixa`:** slug inventado pelo usuário, mesma regra `^[a-z0-9-]+$`, único. Sugestão de forma: `cdb-<emissor>-<aaaa-mm>` (ex.: `cdb-inter-2027-03`). **É chave de `ChaveImportacao`: não renomear depois de importado.**

**`Ativo.Vencimento` opcional.** Nulo significa "sem prazo" (cofrinho, conta remunerada). Um ativo com vencimento continua obrigado a tê-lo (primeira ocorrência no CSV define; as seguintes, se vierem, têm que ser iguais; vazio nas seguintes é aceito como no Passo 1). Um ativo sem vencimento não pode ganhar um depois pelo CSV: linha com vencimento para código cadastrado sem → erro da linha.

**Invariantes:**

- Operação após o `Vencimento` → `OperacaoAposVencimentoException`, **só quando o ativo tem vencimento**. Sem vencimento, qualquer data serve. Para quem tem: a data do resgate no vencimento é o próprio dia, não depois.
- Preço duplicado para (ativo, data) → substitui.

**Banco:** migration tornando a coluna de vencimento de `assets` nula. A coluna `assets.class` já existe (enum como string); só passa a aceitar o valor novo.

## Regras

Nenhuma regra numerada nova. Uma alteração em R7 e três esclarecimentos:

**R7 alterada — dias de ponto comuns.** O conjunto de dias candidatos é calculado **uma vez para a carteira**, não por titular: entra o dia `D` (`de ≤ D ≤ ate`) em que existe `PrecoDiario` com `Data = D` para algum ativo em que **qualquer** titular tem posição > 0 em `D`. Cada titular ganha ponto em todo dia candidato em que ele próprio tem posição > 0 (ou é o dia do seu resgate total, R9) e `D ≥` sua primeira operação. O valor do ponto segue R8/R4, então um titular sem preço novo em `D` repete o valor do ponto anterior — não é interpolação, é o preço mais recente `≤ D`. Motivo: com PU manual, um titular pode ter preço num dia em que o outro não tem; antes do Passo 5 isso só acontecia com operação em dia sem pregão. Sem esta regra, o total da curva cai no dia em que falta um titular.

**R4 para `RendaFixa`:** o PU na data D é o mais recente `≤ D` gravado pela tela. Entre duas atualizações o valor fica constante; a curva mostra degraus. É a informação que existe, sem interpolação.

**R12 (série CDI) para `RendaFixa`:** nada muda — fluxos de caixa são fluxos de caixa.

**Aviso de vencidos (Web, não Core):** ativo **com vencimento** e `Vencimento < hoje` e alguma posição com `Quantidade > 0` em `hoje`. Ativo sem vencimento nunca aparece no aviso. `/carteira` não ganha coluna de vencimento. A página destaca a linha e escreve "venceu em dd/mm/aaaa — lance o resgate". Não altera totais.

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
05/02/2025;vicka;cofrinho-nubank;Caixinha Nubank;;APLICACAO;1;3000,00;0;RENDA_FIXA
10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0;
```

Valores aceitos: `TESOURO`, `RENDA_FIXA`, vazio (= `TESOURO`), sem distinção de caixa. Outro valor é erro da linha. **`vencimento` vazio na primeira ocorrência de um código é aceito só com `classe` = `RENDA_FIXA`** (ativo sem prazo); com `TESOURO` continua erro, como no Passo 1. A classe entra na `LinhaOperacao`; a `Importacao` usa ao criar o ativo. **Ativo que já existe com outra classe → erro da linha** ("classe diverge do cadastro").

**`POST /api/operacoes`** ganha o campo opcional `classe`, mesmas regras.

**`/lancar`:** o select de título ganha a classe entre parênteses; "outro" ganha um select de classe (default Tesouro); com `RendaFixa`, o campo vencimento pode ficar vazio. Abaixo do formulário de operação, um segundo bloco **"Atualizar PU"**: select de ativo (todos), campo PU, campo data (default hoje, não futura), botão. Sucesso: "PU gravado" e a lista dos 10 últimos `PrecoDiario` de ativos de classe `RendaFixa` (por data decrescente) atualiza. `PrecoDiario` não registra origem; um PU digitado para um Tesouro não aparece na lista, e isso é aceito. Erro: mensagem.

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
| 51 | `CsvTradeParser`: `vencimento` vazio na primeira ocorrência com `RENDA_FIXA`; o mesmo com `TESOURO`; código sem vencimento e depois uma linha com vencimento | `Vencimento` nulo aceito; erro da linha; erro da segunda linha |
| 52 | `Calculate`: ativo sem vencimento com aplicação em 2030 e resgate em 2040, `dataReferencia` 2040 | Sem exceção; posição calculada normalmente. Mesmo cenário com vencimento 2035 → `OperacaoAposVencimentoException` |
| 53 | `DailySeries.Build` (R7 alterada): `renato` tem 10 de `tesouro-x` aplicados em 10/01/2025 a 100,00; `vicka` tem 1 de `cdb-y` aplicado em 10/01/2025 a 1.000,00. Preços: `tesouro-x` 100,00 em 10/01 e 101,00 em 11/01; `cdb-y` 1.000,00 em 10/01 só. `de` = 10/01, `ate` = 11/01 | Dois pontos para cada titular. `vicka` em 11/01: `Valor` 1.000,00 (preço de 10/01, R4). `renato` em 11/01: 1.010,00. Um terceiro titular sem operação alguma não ganha ponto |

Testes 1–48 que contam dias por titular podem precisar de novo valor esperado por causa da R7 alterada; cada um que mudar é listado nas decisões de implementação com o motivo.

**Decisões de implementação** (preencher ao escrever os testes).

**Fora dos testes, de propósito:** aviso de vencidos (é filtro na Web), endpoint, formulário, filtro do job. Cobertos pelo e2e.

**E2e — cenário novo em `run.sh`, após o fluxo atual:**

- `operacoes.csv` ganha a 10ª coluna e uma linha de CDB (`cdb-teste-2027-03`, 10 a 1.000,00, vencimento 01/03/2027) uma de CDB já vencido (`cdb-vencido-2025-06`, 5 a 1.000,00, vencimento 30/06/2025, sem resgate) e uma de cofrinho sem vencimento (`cofrinho-teste`, 1 a 500,00).
- Reimporta o CSV **antigo de 9 colunas** → `importadas=0` (compatibilidade).
- `POST /api/precos` com PU 1.047,32 para o CDB → `GET /carteira` contém 10.473,20 na linha do CDB e o total geral subiu exatamente 10.473,20 em relação ao cenário anterior; o backfill de preços continua devolvendo o mesmo `ativos` de antes (o CDB não entrou).
- A página contém "venceu em 30/06/2025" exatamente uma vez (o cofrinho não aparece no aviso).
- `POST /api/precos` com data de amanhã → `400`.

## Tarefas, modelos e prazo

- [ ] **T0 — Pré-requisito.** Passo 4 fechado: "Passo 4 concluído" commitado; polimento P10–P12 feito.
- [ ] **T1 — Lista real.** Levantar na corretora os títulos de renda fixa de vocês: código inventado, nome, vencimento, quantidade, PU de emissão, data de aplicação, PU atual. Planilha fora do repo. Sem código. (30 min)
- [ ] **T2 — Core.** `ClasseAtivo.RendaFixa`; `Ativo.Vencimento` opcional; invariante de vencimento condicional no `PositionCalculator`; parser lê a coluna opcional `classe` e aceita vencimento vazio em `RENDA_FIXA`. Testes 49–52 antes (Opus), implementação depois (Sonnet). Critério: 1–52 verdes. (1–2 sessões)
- [ ] **T3 — Importação, endpoint e job.** Migration (vencimento nulo); `Importacao` usa a classe e o vencimento opcional; `POST /api/precos`; filtro `TesouroDireto` no job e no backfill. Critério: e2e atual verde; `curl` do `POST /api/precos` na VPS grava e a curva muda. (1 sessão)
- [ ] **T4 — Tela.** Classe no `/lancar`, bloco "Atualizar PU", "PU" nos cabeçalhos, aviso de vencidos. Critério: atualizar um PU real pelo navegador e ver na tabela. (1 sessão)
- [ ] **T4b — Dias de ponto comuns.** R7 alterada em `DailySeries`. Teste 53 antes (Opus), implementação depois (Sonnet). Critério: 1–53 verdes; na VPS, `POST /api/snapshots/rebuild` e a curva do total não cai no último dia. (1 sessão)
- [ ] **T5 — Carga real.** Importar a planilha da T1 (CSV de 10 colunas) e atualizar os PUs atuais pela tela. Critério: total geral = Tesouro + renda fixa, conferido com a corretora. (meia sessão)
- [ ] **T6 — E2e + observação.** Cenário novo verde; 2 dias vendo o job ignorar a renda fixa (log sem código inventado). Marcar o critério de pronto. (1 sessão + 2 dias)

Soma: ~6 sessões. Prazo de 10 dias é confortável.

**Modelo por tarefa:**

| Tarefa | Modelo | Por quê |
| --- | --- | --- |
| T1, T5 | nenhum | planilha e tela |
| T2 — escrever os testes 49–52 | Opus 5.5 | regra condicional (vencimento nulo × classe) merece leitura cuidadosa |
| T2 — implementar | Sonnet 5 | loop `dotnet test` |
| T4b — escrever o teste 53 | Opus 5.5 | mudança de regra de série com números exatos |
| T4b — implementar | Sonnet 5 | loop `dotnet test` |
| T3, T4, T6 | Sonnet 5 | casca bem especificada |

**Lista de "ideias para depois"** (preencher, não executar):

- Marcação na curva automática por taxa contratada (prefixado, % CDI, IPCA+), como alternativa ao PU manual.
- Resgate automático no vencimento pelo último PU.
- Histórico de PUs manuais na página, por ativo.
- 401/403 da API do Tesouro encerra o loop do job inteiro (herdado do Passo 4): em 401, pular só o Tesouro e seguir.