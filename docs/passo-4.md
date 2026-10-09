# Passo 4 — Linha do CDI

> Contrato do passo atual. A IA lê só isto, o `CLAUDE.md` e o código. `docs/passo-1.md` a `docs/passo-3.md` são históricos e continuam valendo onde este não os altera.

## Objetivo e critério de pronto

**Frase do passo:** "Na curva, vejo quanto a carteira valeria se cada aplicação tivesse ido para o CDI, e quanto ela rendeu em relação a ele."

**Critério de pronto (todos obrigatórios):**

- [ ] `indices_diarios` tem o CDI de todo dia útil desde a primeira aplicação, carregado uma vez pelo backfill.
- [ ] O job diário busca o CDI do dia sozinho; `ValorCdi` ganha um ponto por dia, por 2 dias seguidos, sem intervenção.
- [ ] A curva mostra uma linha tracejada do total "se fosse CDI", do início até hoje.
- [ ] Abaixo do gráfico: "Se fosse CDI: R$ X · Carteira rendeu Y% do CDI", com X igual à soma dos `ValorCdi` de hoje.
- [ ] Reimportar operação antiga recalcula `ValorCdi` junto com `Valor`, sem passo manual.
- [ ] Core: testes 1–48 verdes. E2e verde com fixture real do BCB.
- [ ] Nenhuma mudança em R1–R9 nem em `DailySeries.Build`.

**Prazo:** 2 semanas a 1h/dia.

## Escopo

**Entra:**

- Entidade `IndiceDiario(indice, data, valor)`, tabela `indices_diarios`. Só `CDI` neste passo; a coluna `indice` existe para `IPCA` e `POUPANCA` entrarem sem migration.
- Cliente da API SGS do Banco Central (série 12, CDI diário) e `POST /api/indices/backfill` (carga inicial). O job diário passa a buscar o CDI do dia junto com os preços.
- `BenchmarkSeries.Build` no Core: função pura que simula a conta CDI por titular a partir das operações e dos índices.
- Coluna `ValorCdi` em `daily_snapshots`, preenchida no mesmo `PreencherSnapshotsAsync`, invalidada pelas mesmas regras.
- Na página: linha tracejada do total CDI; a frase "Se fosse CDI: R$ X · Carteira rendeu Y% do CDI".
- E2e: `fakeapi` serve a fixture real do BCB; cenário confere `ValorCdi` e a frase.

**Fica de fora (não implementar nem "deixar preparado"):**

- IPCA, poupança, Ibovespa, Selic.
- Linha do CDI por titular na curva (só o total). O "% do CDI" por titular pode ir na tabela sem linha — ideia para depois.
- Rentabilidade por período (TWR, MWR), "% do CDI" por mês ou ano.
- Mudar o grão de `daily_snapshots`. O `ValorCdi` segue o grão atual (por titular); quando o grão mudar para (titular, ativo), a mesma regra se aplica por título.
- Qualquer alteração em R1–R9, `PositionCalculator`, `DailySeries`.

## Mudanças no domínio

**Índice diário** (`IndiceDiario`) — uma linha por (índice, dia útil).

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Indice` | string(16) | `CDI` neste passo; chave composta com Data |
| `Data` | DateOnly | dia útil a que a taxa se refere |
| `Valor` | decimal(18,8) | taxa **diária em %** como o BCB publica (ex.: `0,05078800` = 0,050788% ao dia) |

**`SnapshotDiario`** ganha `ValorCdi` (decimal(18,6), nulável). Nulo = ainda não calculado (sem índice para aquele dia); a página trata como "sem CDI".

**No Core:** `BenchmarkSeries.Build` devolve `PontoBenchmark(Data, TitularId, Slug, ValorCdi)`. A Web grava `ValorCdi` na linha de `daily_snapshots` de mesmo (dia, titular); ponto sem linha correspondente é descartado.

**Banco:** tabela `indices_diarios`, chave primária `(indice, date)`. Migration adiciona `valor_cdi` a `daily_snapshots`.

## Regra da conta CDI

**R10 — Fluxos.** Para um titular, cada operação gera um fluxo de caixa na sua data: aplicação **deposita** `Quantidade × PrecoUnitario + Taxas`; resgate **saca** `Quantidade × PrecoUnitario − Taxas`. São os mesmos reais que entraram e saíram da carteira real.

**R11 — Saldo.** Percorrendo os dias corridos de `de` até `ate`, por titular:

```
Saldo(D) = Saldo(D−1) × Fator(D) + Depósitos(D) − Saques(D)
Fator(D) = 1 + Valor(CDI, D) / 100     se existe índice em D
Fator(D) = 1                           se não existe (fim de semana, feriado)
```

Fluxo de um dia entra **depois** da capitalização daquele dia: dinheiro aplicado em D começa a render no dia útil seguinte. Saldo inicial é zero. O saldo pode ficar negativo se um resgate superar o que o CDI teria rendido; isso é um resultado legítimo ("a carteira fez mais que o CDI"), não um erro.

**R12 — Pontos.** Entram os dias `D` com `de ≤ D ≤ ate` tais que `D ≥` primeira operação do titular **e** (existe índice em D **ou** o titular tem operação em D). É o espelho da R7: os mesmos dias em que a série real tem ponto.

**R13 — Na página.** Linha tracejada = `Σ ValorCdi` dos titulares por dia (dias em que todos os titulares com ponto têm `ValorCdi`). "Se fosse CDI: R$ X" = `Σ ValorCdi` de hoje. "Carteira rendeu Y% do CDI" = `(ValorHoje − Caixa) / (ValorCdiHoje − Caixa)`, com `Caixa = Σ depósitos − Σ saques` de todos os titulares até hoje. Se `ValorCdiHoje − Caixa ≤ 0`, mostra "—".

**Exemplo de referência (vira o teste 41)** — titular `renato`, título fictício a R$ 1.000,00 por unidade, CDI fixo em 0,05% ao dia nos cinco dias úteis de 06/01/2025 (segunda) a 10/01/2025 (sexta), taxas 0:

| Dia | Operação | Fluxo | Saldo CDI |
| --- | --- | --- | --- |
| 06/01 | aplica 1,0 a 1.000,00 | +1.000,00 | 1.000,00 |
| 07/01 | — | — | 1.000,50 |
| 08/01 | aplica 0,5 a 1.000,00 | +500,00 | 1.501,00 |
| 09/01 | — | — | 1.501,75 |
| 10/01 | resgata 0,3 a 1.000,00 | −300,00 | 1.202,50 |

`Build(…, de = 01/01/2025, ate = 10/01/2025)` devolve exatamente esses cinco pontos (comparação com 2 casas; internamente `decimal` sem arredondar: 10/01 = 1.202,501625).

**Extensão (teste 42):** índice também em 13/01/2025 (segunda) com 0,05%; nada em 11 e 12/01 → nenhum ponto no fim de semana; 13/01 = 1.202,501625 × 1,0005 = **1.203,10**.

## Fonte do CDI e job

**API SGS do Banco Central**, série 12 (CDI, taxa diária), sem autenticação:

`GET https://api.bcb.gov.br/dados/serie/bcdata.sgs.12/dados?formato=json&dataInicial=dd/MM/yyyy&dataFinal=dd/MM/yyyy`

Resposta esperada: `[{ "data": "02/01/2017", "valor": "0.050788" }, …]` — `data` em `dd/MM/yyyy`, `valor` **string com ponto decimal**, um registro por dia útil. Conferido na T1 (09/10/2026, fixtures `cdi-2025-01.http`, `cdi-5anos.http` e `cdi-tudo.http`): valor é string com ponto decimal, data em `dd/MM/yyyy`, um registro por dia útil, content-type `application/json`, headers `etag` e `cache-control`. Cinco anos (2017–2021) vieram inteiros, 51 KB. O intervalo 2017–2026 foi RECUSADO: o SGS devolve HTTP 200 com content-type `text/html` e a página "Requisição inválida" (limite de 10 anos por chamada em série diária). Por isso: (1) o cliente trata content-type diferente de `application/json` como falha e nunca desserializa; (2) o backfill fatia em janelas de 5 anos. A fixture `cdi-tudo.http` fica como caso de recusa.

**`POST /api/indices/backfill`:** `dataInicial` = menor data de operação, `dataFinal` = hoje; se o intervalo passar do limite da T1, divide em fatias. Grava `IndiceDiario(CDI, data, valor)` substituindo existentes. Devolve `{ indice, registros, de, ate }`. Ao terminar, apaga todos os `daily_snapshots` e dispara o preenchimento.

**Job diário:** logo após os preços, busca o CDI dos últimos 7 dias corridos que não estão em `indices_diarios` (uma chamada com `dataInicial`/`dataFinal` cobrindo o intervalo) e grava. Falha de rede ou 5xx → `Warning`, tenta no próximo ciclo; não derruba o job de preços. Depois, o preenchimento de snapshots passa a calcular `ValorCdi` junto com `Valor`.

**Preenchimento (`PreencherSnapshotsAsync`):** além do que já faz, chama `BenchmarkSeries.Build(titulares, operacoes, indices, de, ate)` e grava `ValorCdi` nas linhas de `daily_snapshots` cujo (dia, titular) existe e cujo `ValorCdi` ainda é nulo. Linha nova nasce com os dois valores. Invalidação por importação e `rebuild` continuam como estão: apagar linhas apaga `Valor` e `ValorCdi` juntos.

## Gráfico e página

- Uma `<polyline>` a mais, tracejada (`stroke-dasharray`), cinza, com o total CDI por dia. Legenda ganha "CDI".
- Escala do eixo Y considera o maior entre o total real e o total CDI.
- Abaixo da linha "Valor hoje …", uma segunda: "Se fosse CDI: R$ X · Carteira rendeu Y% do CDI" (R13). Com `ValorCdi` nulo em algum titular hoje, mostra "Se fosse CDI: sem dado".

Tudo em `GraficoSvg` e na página; sem JS, sem biblioteca, sem CSS novo.

## Casos de teste

Lista fechada. Testes 1–40 não mudam. Os novos são só de `BenchmarkSeries.Build`.

| # | Caso | Resultado esperado |
| --- | --- | --- |
| 41 | Exemplo de referência (5 dias úteis, 3 operações) | 5 pontos com os saldos da tabela (2 casas) |
| 42 | Fim de semana sem índice, segunda com índice | Sem ponto em 11 e 12/01; 13/01 = 1.203,10 |
| 43 | Índice em dias anteriores à primeira operação do titular | Nenhum ponto antes da primeira operação; saldo começa em zero |
| 44 | Dois titulares com operações em datas diferentes | Contas independentes; cada série começa na sua primeira operação |
| 45 | Resgate maior que o saldo CDI | Saldo negativo, sem exceção |
| 46 | Aplicação com taxas 10,00 e resgate com taxas 10,00 | Depósito 1.010,00; saque 290,00 |
| 47 | Operação em dia sem índice (feriado) | Ponto existe nesse dia, fator 1; dias seguintes continuam normalmente |
| 48 | `de > ate`, ou lista de operações vazia | Lista vazia, sem exceção |

**Decisões de implementação** (preencher ao escrever os testes, como nos passos anteriores).

**E2e — cenário novo em `run.sh`:**

- `fakeapi` serve `/dados/serie/bcdata.sgs.12/dados` com a fixture real gravada na T1 (corpo e headers byte a byte), ignorando os parâmetros de data. A Web recebe `Bcb__BaseUrl=http://fakeapi`.
- Após o fluxo atual: `POST /api/indices/backfill` → `registros` = número de itens da fixture; `GET /carteira` contém a `<polyline>` tracejada e a frase "Se fosse CDI: R$"; o valor X é conferido contra um cálculo em `jq` da R11 com os fluxos do `operacoes.csv` e as taxas da fixture (mesma técnica usada para os preços).
- Reimportação com operação antiga a mais → a frase muda (invalidação recalculou `ValorCdi`).

## Tarefas, modelos e prazo

- [ ] **T0 — Pré-requisito.** Passo 3 fechado e commitado como "Passo 3 concluído".
- [x] **T1 — Contrato do BCB.** `curl -i` na série 12 para um mês e para o intervalo inteiro desde a primeira aplicação; gravar `tests/e2e/fixtures/cdi-*.http`; anotar formato, limite de intervalo e headers na seção "Fonte do CDI". Sem código. (30 min)
- [ ] **T2 — `BenchmarkSeries.Build`.** Testes 41–48 antes (Opus), implementação depois (Sonnet). Critério: 1–48 verdes. (2 sessões)
- [ ] **T3 — `IndiceDiario`, cliente BCB, backfill, job.** Migration da tabela; cliente HTTP tipado; `POST /api/indices/backfill`; busca diária no job. Critério: na VPS, `indices_diarios` tem o CDI desde 2017; log do job do dia seguinte mostra uma chamada ao BCB; o cliente rejeita resposta text/html com erro claro no log. (2 sessões)
- [ ] **T4 — `ValorCdi` nos snapshots.** Migration da coluna; cálculo no preenchimento; `rebuild`. Critério: na VPS, após rebuild, nenhum `ValorCdi` nulo em dia com índice. (1 sessão)
- [ ] **T5 — Página.** Linha tracejada e a frase. Critério: X = `Σ ValorCdi` de hoje no banco; Y coerente (carteira do Tesouro perto de 100% do CDI é o esperado). (1 sessão)
- [ ] **T6 — E2e + observação.** Cenário novo verde; 2 dias vendo o CDI chegar sozinho. Marcar o critério de pronto. (1 sessão + 2 dias)

Soma: ~8 sessões. Prazo de 2 semanas tem folga.

**Modelo por tarefa:**

| Tarefa | Modelo | Por quê |
| --- | --- | --- |
| T1 | nenhum | `curl` e leitura |
| T2 — escrever os testes 41–48 | Opus 5.5 | os saldos têm que sair exatos do exemplo |
| T2 — implementar | Sonnet 5 | loop `dotnet test` |
| T3, T4, T5, T6 | Sonnet 5 | casca bem especificada |

**Lista de "ideias para depois"** (preencher, não executar):

- "% do CDI" por titular na tabela.
- IPCA e poupança como índices (mensais: exigem regra de periodicidade). Ibovespa só com ações.
- Grão de `daily_snapshots` por (titular, ativo), quando aparecer a primeira necessidade de série por título ou por classe. `ValorCdi` segue junto, pela mesma regra por título.
- Rentabilidade por período (TWR) e "% do CDI" no ano.