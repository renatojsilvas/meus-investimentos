# Passo 6 — Ações e FII com histórico fiel

> Contrato do passo atual. A IA lê só isto, o `CLAUDE.md` e o código. `docs/passo-1.md` a `docs/passo-5.md` são históricos e continuam valendo onde este não os altera.

## Objetivo e critério de pronto

**Frase do passo:** "Minhas ações e FIIs estão na carteira desde a primeira compra, com o fechamento de cada pregão e os desdobramentos aplicados na data certa, e a curva mostra tudo junto."

**Critério de pronto (todos obrigatórios):**

- [ ] Todas as ações e FIIs de vocês estão no banco, com classe `Acao` ou `Fii`, com as compras, vendas e eventos de capital nas datas reais (extrato de movimentação da B3).
- [ ] `daily_prices` tem o fechamento de todo pregão de cada ticker desde a primeira compra, vindo do COTAHIST anual (backfill), e o job diário grava o fechamento do pregão anterior sozinho, vindo do COTAHIST diário, por 2 dias seguidos.
- [ ] Para um papel que desdobrou, a curva não salta no dia do evento e a quantidade na página é a da corretora hoje.
- [ ] `/carteira` mostra ações e FIIs dentro do bloco do titular, com quantidade, PU (fechamento), valor e rentabilidade; o total geral inclui tudo; a conferência com a posição da Área do Investidor da B3 bate em quantidade e preço de fechamento.
- [ ] `/lancar` aceita os três tipos de evento e as duas classes novas.
- [ ] Tesouro e renda fixa continuam exatamente como no Passo 5: job, backfill, PU manual, CSVs antigos reimportam com `importadas=0`.
- [ ] Core: testes 1–58 verdes. E2e verde com o cenário de ações.

**Prazo:** 4 semanas a 1h/dia. Passou, o escopo estava errado: cortar, não esticar.

## Escopo

**Entra:**

- `ClasseAtivo.Acao` e `ClasseAtivo.Fii`. `Codigo` = ticker em minúsculo (`petr4`, `hglg11`). Sem vencimento, sempre.
- `TipoOperacao.Desdobramento`, `TipoOperacao.Grupamento`, `TipoOperacao.Bonificacao`: eventos de capital **como operação**, na data do evento, pelo mesmo CSV e pela mesma tela. É a única mudança de modelo do passo.
- R14 (eventos ajustam quantidade e custo) no `PositionCalculator`. R10 e R12 esclarecidas: evento não é fluxo de caixa.
- Fonte de preço: arquivos COTAHIST da B3, atrás de uma interface (`ICotacoesB3Client`) para troca futura por API própria. Backfill pelos arquivos anuais; job diário pelo arquivo do dia.
- `POST /api/cotacoes/backfill`.
- `/carteira`: aviso de salto de preço (Web) para pegar evento não lançado.
- E2e: ações no CSV com um desdobramento; fakeapi servindo recortes reais do COTAHIST.

**Fica de fora (não implementar nem "deixar preparado"):**

- Proventos em dinheiro: dividendos, JCP, rendimento de FII, amortização, cessão de direitos. Não mudam posição nem curva de valor; mudam rentabilidade e IR. **Passo 7.** (O extrato já tem todos com valor: 1.421 linhas prontas para quando chegar.)
- Corretagem e emolumentos de ações (o extrato da B3 não traz; `taxas` 0).
- Fonte automática de eventos (API própria ou scraping da B3). Evento entra pelo extrato, à mão. Ideia para depois.
- Subscrição, cisão, incorporação, conversão de classe (ON↔PN), ETF, BDR, opções, aluguel.
- Preço ajustado por evento (tipo Yahoo). O preço é o fechamento como foi, sempre.
- Corretora na operação. Mesmo ticker em duas corretoras soma numa posição.
- Importar o xlsx da B3 direto. A conversão para o CSV é feita fora do app (Renato), como nos passos anteriores.
- Qualquer mudança em R1–R9, R11, R13, `DailySeries`, `BenchmarkSeries` além do esclarecimento de R10/R12 abaixo.

## Mudanças no domínio

**`ClasseAtivo`** ganha `Acao` e `Fii`. Diferença entre as duas neste passo: nenhuma no cálculo. Existem separadas porque o IR (Passo 10) e os proventos (Passo 7) tratam diferente, e é um valor de enum. No CSV: `ACAO`, `FII`.

**`TipoOperacao`** ganha `Desdobramento`, `Grupamento`, `Bonificacao`. Os nomes entram na `ChaveImportacao` como os de hoje. No CSV: `DESDOBRAMENTO`, `GRUPAMENTO`, `BONIFICACAO`.

**Semântica das colunas por tipo** (o `Operacao` não muda de forma; os campos mudam de sentido):

| Tipo | `quantidade` | `preco_unitario` | `taxas` |
| --- | --- | --- | --- |
| `APLICACAO` / `RESGATE` | unidades | preço | corretagem/emolumentos |
| `DESDOBRAMENTO` | **unidades recebidas** (o extrato da B3 credita a diferença: 100 viram 1.000 = `900`) | vazio ou 0 | 0 |
| `GRUPAMENTO` | **unidades retiradas** (1.100 viram 110 = `990`) | vazio ou 0 | 0 |
| `BONIFICACAO` | unidades recebidas | custo unitário atribuído pela empresa (o informado para IR); **0 permitido** quando não se sabe | 0 |

**`Codigo` de `Acao`/`Fii`:** o ticker da B3 em minúsculo, `^[a-z0-9]+$` (sem hífen: `petr4`, `hglg11`, `itsa4`). Vale a mesma regra de slug do Passo 5 (regex geral `^[a-z0-9-]+$` continua; o ticker simplesmente não tem hífen). É chave de importação; não renomear.

**Vencimento:** `Acao` e `Fii` nunca têm. No CSV, `vencimento` preenchido nessas classes é erro da linha ("ação não tem vencimento").

**Invariantes novas:**

- Evento em (titular, ativo) sem posição (`Quantidade = 0`) na data → `PosicaoInsuficienteException` (a mesma do resgate), mensagem "evento em X em dd/mm/aaaa sem posição".
- `PrecoUnitario`: `> 0` para `Aplicacao` e `Resgate`; `≥ 0` para `Bonificacao`; **`= 0` obrigatório** para `Desdobramento` e `Grupamento` (a validação atual "PrecoUnitario > 0 para toda operação" passa a depender do tipo).
- `Quantidade` `> 0` em todo tipo. Grupamento com `Quantidade > Q` → `PosicaoInsuficienteException`, como o resgate.
- Decisão: evento é **delta de quantidade**, não fator, porque é assim que o extrato de movimentação da B3 informa ("Desdobro — Crédito — 441"); o usuário copia o número, não calcula.

**Banco:** nenhuma migration. `assets.classe` e `trades.tipo` são string (enum por conversão); só passam a aceitar os valores novos.

## Regras

**R14 — Eventos de capital.** Para o estado (titular, ativo) com quantidade `Q`, custo total `C` e realizado `R`, na data do evento:

- `Desdobramento` de `q` unidades: `Q' = Q + q`; `C` e `R` não mudam. PU médio passa a `C / Q'`.
- `Grupamento` de `q` unidades: `Q' = Q − q`; `C` e `R` não mudam.
- `Bonificacao` de `q` unidades a custo unitário `p`: `Q' = Q + q`; `C' = C + q × p`; `R` não muda.

**Ordem dentro do mesmo dia:** eventos primeiro, depois aplicações, depois resgates. (A B3 aplica o evento na abertura do pregão "ex"; uma venda nesse dia já é em quantidade nova.) Hoje a ordem é `Aplicacao` antes de `Resgate`; passa a ser evento (0), aplicação (1), resgate (2).

**R4 continua:** o preço na data D é o mais recente `≤ D`, sem ajuste. Como o COTAHIST não ajusta o fechamento e a quantidade muda no mesmo dia pela R14, `Q × PU` não salta no dia do evento. Sem a linha do evento, salta — é a conferência visual.

**R10 e R12 esclarecidas (`BenchmarkSeries`):** evento **não gera fluxo** de caixa (nem depósito nem saque) e **não é operação** para fins de R12 (dia de evento sem índice não vira ponto do CDI). Hoje o código trata "não é aplicação" como saque; passa a tratar só `Resgate` como saque e só `Aplicacao` como depósito.

**R7 (`DailySeries`):** nada muda; o dia do evento é dia com operação, logo candidato. O ponto não salta pela R14 + R4.

**Aviso de salto (Web, não Core):** para cada ativo `Acao`/`Fii` com posição hoje, se nos últimos 60 dias existem dois preços consecutivos (em `daily_prices`) com razão `< 0,6` ou `> 1,67` e não existe evento desse ativo entre as duas datas (para qualquer titular), a página mostra na célula do título, em negrito: "preço mudou N% em dd/mm/aaaa — confira desdobramento ou grupamento". Não altera totais. É o detector do evento esquecido.

**Exemplo de referência (vira o teste 54)** — titular `renato`, ativo `acao-x` classe `Acao`, sem taxas:

| Data | Operação | Q após | Custo após (R$) | PU médio | Fechamento no dia | Valor | Rentab. |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 10/03/2025 | APLICACAO 100 a 40,00 | 100 | 4.000,00 | 40,0000 | — | — | — |
| 13/03/2025 | — | 100 | 4.000,00 | 40,0000 | 50,00 | 5.000,00 | 1.000,00 (25,00%) |
| 14/03/2025 | DESDOBRAMENTO 900 | 1.000 | 4.000,00 | 4,0000 | 5,00 | 5.000,00 | 1.000,00 |
| 20/03/2025 | — | 1.000 | 4.000,00 | 4,0000 | 5,20 | 5.200,00 | 1.200,00 (30,00%) |
| 01/04/2025 | BONIFICACAO 100 a 2,00 | 1.100 | 4.200,00 | 3,8182 | 5,00 | 5.500,00 | 1.300,00 (30,95%) |
| 10/04/2025 | GRUPAMENTO 990 | 110 | 4.200,00 | 38,1818 | 50,00 | 5.500,00 | 1.300,00 |
| 15/04/2025 | RESGATE 10 a 52,00 | 100 | 3.818,18 | 38,1818 | 52,00 | 5.200,00 | 1.381,82 (36,19%) |

No resgate de 15/04: custo baixado = 10 × 4.200 / 110 = 381,82; resultado realizado = 520,00 − 381,82 = **138,18**. Preços: só os da coluna "Fechamento no dia", nas datas indicadas.

Mesmo exemplo **sem a linha de 14/03**: em 20/03 o valor seria 100 × 5,20 = 520,00 e a rentabilidade −3.480,00. É o erro que a R14 evita.

## Fonte dos eventos: extrato de movimentação da B3

Conferido em 18/10/2026 com o extrato real do Renato (Área do Investidor → Extratos → Movimentação, 1.840 linhas de 03/2021 a 10/2026) contra as posições exportadas no mesmo dia. Colunas: `Entrada/Saída, Data, Movimentação, Produto, Instituição, Quantidade, Preço unitário, Valor da Operação`. Todas as compras e vendas vêm com preço; eventos vêm com `-`. É a única fonte necessária; o extrato de negociação não é usado. Não traz corretagem nem emolumentos (`taxas` = 0; na XP a corretagem é zero). Mapeamento fechado:

| Movimentação (B3) | Linha no CSV | Observação |
| --- | --- | --- |
| `Transferência - Liquidação` (Crédito / Débito) | `APLICACAO` / `RESGATE` com `Preço unitário` | 162 linhas, todas com preço; `taxas` 0 |
| `Desdobro` (Crédito) | `DESDOBRAMENTO` com a quantidade creditada | ex.: GGRC11 441 em 07/03/2024; WEGE3 26 em 29/04/2021 |
| `Bonificação em Ativos` (Crédito, pode ter fração) | `BONIFICACAO` com a quantidade creditada e o custo unitário informado pela empresa (0 se não souber) | ex.: ITUB3 28,5 em 19/03/2025; RADL3 6,24. O extrato não traz o custo |
| `Fração em Ativos` (Débito) | nada | a fração sai na linha seguinte |
| `Leilão de Fração` (Crédito, com preço) | `RESGATE` da fração ao preço do leilão, na data do leilão | ex.: ITUB3 0,39 a 44,873 em 10/03/2026; entre a fração e o leilão a posição fica com a fração a mais, aceito |
| `Atualização` (Crédito) com quantidade **menor** que a posição | `APLICACAO` ao preço da subscrição (aviso da corretora; o extrato não traz), na data da linha | é a cota nova de subscrição chegando (HGLG11 2+2, HCTR11 4+3+2, VRTA11 4, XPIN11 2, VISC11 1) |
| `Atualização` (Crédito) com quantidade **igual** à posição | nada | reescrituração administrativa (AZZA3 360 ×2, B3SA3 500, WEGE3 91 ×2, XPIN11 93) |
| `Atualização` (Crédito) em **ticker novo** com a posição inteira | `RESGATE` do ticker antigo ao PU médio + `APLICACAO` do novo ao mesmo PU médio, mesma data | troca de ticker/incorporação: ARZZ3→AZZA3 112 em 02/08/2024; ODPV3→SAUD3 572 em 06/05/2026. Realizado 0; o COTAHIST tem o antigo até a troca e o novo depois |
| `Cisão` (Crédito) no ticker de origem | nada | administrativa (ITUB3 85 em 05/10/2021) |
| `Cisão` ou `Atualização` (Crédito) em ticker novo, sem compra | `BONIFICACAO` com custo = o atribuído no comunicado (ou parcela proporcional do custo da origem) | XPLG11 30 em 23/07/2026 (incorporação parcial de XPIN11; quando XPIN11 for extinto, `RESGATE 93` pelo valor recebido, ou `GRUPAMENTO 93` se nada vier). XPAR3 85 em 05/10/2021 virou XPBR31 e foi vendido: **fica fora da carga** (posição encerrada; só realizado/IR, Passo 11); o custo de ITUB3 fica um pouco alto por não abater a parcela cindida — aceito |
| `Transferência` (Crédito e Débito iguais, mesma data) | nada | custódia mudando de corretora; sem corretora no modelo |
| `Direito de Subscrição`, `Cessão de Direitos`, `Solicitação`, `Exercido`, `Não Exercido`, `Recibo de Subscrição` (códigos 12/13/14) | nada | transitórios; o resultado aparece como `Atualização` no código 11. Dinheiro da cessão é Passo 7 |
| `Rendimento`, `Dividendo`, `Juros Sobre Capital Próprio`, `Amortização` | nada | Passo 7 |

Conferência feita: para todos os 32 tickers em carteira, `Σ Liquidação ± eventos acima` = posição da B3, incluindo XPLG11 (30 cotas vindas da incorporação parcial de XPIN11, linha `Atualização` de 23/07/2026). O extrato começa em 04/03/2021: compras anteriores precisam de outra fonte (nota de corretagem) ou de uma linha de carga inicial na data mais antiga que se souber.

## Fonte de preço: COTAHIST da B3

Arquivos públicos, sem autenticação:

- Anual: `https://bvmf.bmfbovespa.com.br/InstDados/SerHist/COTAHIST_A<AAAA>.ZIP` — todos os pregões do ano, todos os papéis (~100 MB; o do ano corrente cresce até dezembro).
- Diário: `https://bvmf.bmfbovespa.com.br/InstDados/SerHist/COTAHIST_D<DDMMAAAA>.ZIP` — um pregão (~1 MB). Dia sem pregão → `404`, e isso **não é erro**.

Dentro do zip, um `.TXT` de largura fixa (245 caracteres por linha, Latin-1). Só interessam as linhas de registro `01` com `TPMERC = 010` (mercado à vista) e `CODNEG` igual ao ticker em maiúsculo (comparação exata após `Trim`; `PETR4F` é fracionário e não casa). Campos, posições 1-based, **a confirmar na T1 contra o layout oficial e uma linha real**:

| Campo | Posições | Formato |
| --- | --- | --- |
| `TIPREG` | 1–2 | `01` |
| `DATA` | 3–10 | `AAAAMMDD` |
| `CODBDI` | 11–12 | `02` lote padrão, `12` FII (não filtrar por ele; só anotar) |
| `CODNEG` | 13–24 | ticker, completado com espaços |
| `TPMERC` | 25–27 | `010` = vista |
| `PREULT` | 109–121 | fechamento, 13 dígitos, 2 decimais implícitos (`0000000005000` = 50,00) |

Fixtures em `tests/e2e/fixtures/`: `cotahist-a2025.http` (o `curl -i` real, só headers + primeiras linhas, para documentar) e `cotahist-a2025-recorte.zip` / `cotahist-d-recorte.zip` (zips reais reduzidos às linhas dos tickers do e2e — gerados na T1 com `unzip -p … | grep` e re-zipados). O fakeapi serve os zips com `Content-Type: application/zip`.

**Cliente (`Carteira.Web`):**

```csharp
public interface ICotacoesB3Client
{
    Task<IReadOnlyList<CotacaoB3Dto>> GetFechamentosAsync(
        IReadOnlyCollection<string> tickers, DateOnly de, DateOnly ate, CancellationToken ct);
}
public record CotacaoB3Dto(string Ticker, DateOnly Data, decimal Fechamento);
```

`CotahistClient` implementa: se `de == ate`, baixa o diário daquele dia (`404` → lista vazia); senão, baixa o anual de cada ano em `[de, ate]` e filtra as datas. Lê o zip em stream, linha a linha, guardando só os tickers pedidos. `BaseUrl` em `Cotahist:BaseUrl` (default o da B3). Registrado em `Program.cs` como os outros clientes.

**Troca de fonte (decisão de desenho):** a API própria do Renato, quando existir, é outra classe implementando `ICotacoesB3Client`, registrada no lugar de `CotahistClient`, com sua própria fixture. Nada em `Importacao`, `PriceSyncJob`, Core ou página muda. Por isso a interface recebe tickers e intervalo, não URLs nem arquivos.

**Job e backfill (`PriceSyncJob`):**

- Diário, após Tesouro e CDI: tickers = ativos `Acao`/`Fii` com posição > 0 ontem; `GetFechamentosAsync(tickers, ontem, ontem)`; grava `PrecoDiario` substituindo. Lista vazia = sem pregão, log informativo, não é falha. Erro HTTP (não 404) → log de erro e **o loop continua** (não derruba Tesouro nem CDI; é a resposta à ideia herdada do 401).
- `POST /api/cotacoes/backfill`: para cada ativo `Acao`/`Fii`, de = primeira operação dele (qualquer titular), ate = ontem; um `GetFechamentosAsync` por ativo; grava só (ativo, dia) que não existe. Resposta `{ "ativos": n, "registros": m }`. Idempotente. Chama `ReconstruirSnapshotsAsync(menor de)` no fim. `BackfillPrecosAsync` (Tesouro) não muda.
- Para não baixar o mesmo anual uma vez por ativo, o backfill chama o cliente **uma vez com todos os tickers** e o intervalo `[menor primeira operação, ontem]`, e distribui. É a única otimização permitida.

## Importação e tela

**CSV:** mesmas 10 colunas. Novidades: `classe` aceita `ACAO` e `FII`; `tipo` aceita os três eventos; `preco_unitario` vazio aceito **só** em `DESDOBRAMENTO`/`GRUPAMENTO` (vira 0); `vencimento` tem que estar vazio em `ACAO`/`FII`. Exemplo:

```csv
data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas;classe
10/03/2025;renato;acao-x;Ação X ON;;APLICACAO;100;40,00;0;ACAO
14/03/2025;renato;acao-x;Ação X ON;;DESDOBRAMENTO;900;;0;ACAO
01/04/2025;renato;acao-x;Ação X ON;;BONIFICACAO;100;2,00;0;ACAO
10/04/2025;renato;acao-x;Ação X ON;;GRUPAMENTO;990;;0;ACAO
15/04/2025;renato;acao-x;Ação X ON;;RESGATE;10;52,00;0;ACAO
05/05/2025;vicka;hglg11;HGLG11;;APLICACAO;20;160,50;0;FII
```

Evento é por titular: se os dois tiverem o mesmo papel, cada um tem sua linha de evento (o extrato da B3 é por CPF, então sai assim naturalmente). Hoje só o Renato tem ações e FII.

**`ChaveImportacao`:** sem mudança de fórmula. Para evento: `titular|codigo|data|Desdobramento|900|0`.

**`Importacao`:** cria ativos `Acao`/`Fii` sem vencimento; classe divergente do cadastro continua erro; para `Acao`/`Fii` **não** grava PU de emissão (P15 é só `RendaFixa` — o preço vem do COTAHIST). Validação de invariantes continua sendo `PositionCalculator.Calculate` sobre existente + novo (a R14 e a ordem no dia valem na importação).

**`POST /api/operacoes`:** mesmos campos; `tipo` e `classe` aceitam os valores novos.

**`/lancar`:** select de classe ganha Ação e FII (vencimento desabilitado nelas); select de tipo ganha os três eventos; com evento selecionado, o rótulo de quantidade vira "Unidades recebidas" (desdobramento/bonificação) ou "Unidades retiradas" (grupamento) e o campo preço é desabilitado nos dois primeiros. Só rótulos e `disabled`; a validação continua no parser.

## Página

- Ações e FIIs nas tabelas por titular e consolidado, junto das demais linhas; "PU" é o fechamento. Nenhuma coluna nova.
- Aviso de salto na célula do título, em negrito, como o aviso de vencido. Sem CSS novo.
- Nada muda no `GraficoSvg`.

## Casos de teste

Lista fechada. Testes 1–53 não mudam.

| # | Caso | Resultado esperado |
| --- | --- | --- |
| 54 | Exemplo de referência: `Calculate` em 13/03, 20/03, 01/04, 10/04 e 15/04/2025 | As colunas Q, custo, PU médio, valor e rentabilidade da tabela (2 casas; percentual 4 casas); realizado 138,18 em 15/04. Variante sem a linha de 14/03 em 20/03 → valor 520,00 |
| 55 | `CsvTradeParser`: linhas com `DESDOBRAMENTO` e `GRUPAMENTO` com preço vazio; `BONIFICACAO` com preço 2,00 e outra com preço vazio; `classe` `ACAO` e `FII`; `vencimento` preenchido em `ACAO`; `APLICACAO` com preço vazio; `DESDOBRAMENTO` com preço 5,00 | Tipos e classes certos, preço 0 nos vazios; erro "ação não tem vencimento"; erro de preço na aplicação; erro "desdobramento não tem preço" |
| 56 | `Calculate`: desdobramento em ativo sem posição; grupamento de 200 com posição 100; desdobramento e resgate **no mesmo dia** (100 a 40,00 em 10/03; em 14/03 desdobramento 900 e resgate de 500 a 5,00) | `PosicaoInsuficienteException` nos dois primeiros; no terceiro, sem exceção, Q 500, custo 2.000,00, realizado 500,00 |
| 57 | `BenchmarkSeries.Build` com aplicação de 1.000,00 em 10/03/2025, desdobramento de 900 em 14/03 e bonificação a 2,00 em 01/04; CDI 0,05% em todo dia de 11/03 a 02/04 **exceto** 14/03 e 01/04 | Só um fluxo (o de 10/03); nenhum ponto em 14/03 nem em 01/04 (evento não é operação para R12 e não há índice nesses dias); saldo em 02/04 = 1.000 × 1,0005^21 = 1.010,552667 (6 casas) |
| 58 | `DailySeries.Build` com o exemplo de referência de 13/03 a 14/03 | Dois pontos, `Valor` 5.000,00 nos dois; `Custo` 4.000,00 nos dois |

**Decisões de implementação** (preencher ao escrever os testes).

**Fora dos testes, de propósito:** cliente COTAHIST (parser de largura fixa), backfill, job, aviso de salto, tela. Cobertos pelo e2e.

**E2e — cenário novo em `run.sh`, após o de renda fixa:**

- `operacoes.csv` ganha linhas de `ACAO`: um ticker real do recorte da fixture com compra antes de um pregão da fixture e, se a T1 achar um papel com evento real dentro do período da fixture, o evento na data real; senão, compra + desdobramento fictício (900 unidades sobre 100) numa data sem preço na fixture, e o aviso de salto fica fora do e2e.
- `POST /api/cotacoes/backfill` → `ativos` = número de tickers de ação/FII do CSV; `registros` = número de linhas do recorte para esses tickers.
- `GET /carteira`: linha do ticker com quantidade pós-evento × fechamento da fixture no último pregão (valor conferido por `jq`/`awk` lendo o recorte, como nos outros cenários); total geral subiu exatamente esse valor.
- `POST /api/prices/sync` → o job chama o diário: fakeapi responde o `cotahist-d-recorte.zip` para qualquer `COTAHIST_D*.ZIP`; `daily_prices` ganha a data do recorte diário (conferido via página ou endpoint existente).
- Reimporta os CSVs antigos (9 e 10 colunas) → `importadas=0`.
- `POST /api/operacoes` com `DESDOBRAMENTO` em ticker sem posição → `400` com "sem posição".

## Tarefas, modelos e prazo

- [ ] **T0 — Pré-requisito.** Passo 5 fechado ("Passo 5 concluído" commitado); polimento P13–P16 feito.
- [ ] **T1 — Fonte e extratos.** (a) `curl -i` no anual mais recente e num diário da B3, a partir da VPS: URL, tamanho, content-type, `404` em dia sem pregão; `unzip -p` e conferir as posições da tabela com uma linha de `PETR4` e uma de um FII; gravar `cotahist-a2025.http` (headers + 5 linhas) e os dois zips de recorte com os tickers de vocês + os do e2e. (b) **Feito em 18/10/2026:** extrato de movimentação do Renato conferido (seção "Fonte dos eventos"). Vicka não tem ações nem FII. Para a T5 faltam só os preços das 5 cotas de subscrição e os custos atribuídos das 9 bonificações (avisos da corretora/empresa, ou 0). (c) Lista dos tickers. Sem código. (1 h)
- [ ] **T2 — Core.** Enums, R14, ordem no dia, validação de preço por tipo, parser, R10/R12 em `BenchmarkSeries`. Testes 54–58 antes (Opus), implementação depois (Sonnet). Critério: 1–58 verdes. (2–3 sessões)
- [ ] **T3 — Cliente e backfill.** `ICotacoesB3Client`, `CotahistClient`, `POST /api/cotacoes/backfill`, fakeapi servindo os zips. Critério: na VPS, backfill grava o histórico dos tickers reais; `select count(*) from daily_prices` por ticker ≈ pregões desde a primeira compra. (2 sessões)
- [ ] **T4 — Importação, job e tela.** `Importacao`/`/api/operacoes` com classes e tipos novos; job diário chamando o diário sem derrubar o loop; `/lancar` com os rótulos; aviso de salto em `/carteira`. Critério: e2e atual verde; na VPS, `POST /api/prices/sync` grava o fechamento de ontem dos tickers. (2 sessões)
- [ ] **T5 — Carga real.** Converter o extrato de movimentação para o CSV por script (um por titular, ordem por data; as 17 linhas de `Atualização` conferidas uma a uma; preços de subscrição e custos de bonificação preenchidos à mão), importar, backfill, conferir quantidade e fechamento de cada ticker com a posição da B3. Critério: todos batem; nenhum aviso de salto sem evento correspondente. (1–2 sessões)
- [ ] **T6 — E2e + observação.** Cenário novo verde; 2 dias vendo o job gravar o fechamento sozinho (log com os tickers, sem erro). Marcar o critério de pronto. (1 sessão + 2 dias)

Soma: ~10 sessões. Prazo de 4 semanas.

**Modelo por tarefa:**

| Tarefa | Modelo | Por quê |
| --- | --- | --- |
| T1, T5 | nenhum | `curl`, planilha, conferência |
| T2 — escrever os testes 54–58 | Opus 5.5 | exemplo de referência com três eventos e números exatos |
| T2 — implementar | Sonnet 5 | loop `dotnet test` |
| T3, T4, T6 | Sonnet 5 | casca bem especificada; o parser de largura fixa é mecânico |

**Lista de "ideias para depois"** (preencher, não executar):

- Herdadas do Passo 5: "Atualizar PU" por saldo; apagar ativo e operações pela tela; marcação na curva por taxa contratada; resgate automático no vencimento; histórico de PUs manuais na página.
- Fonte automática de eventos (API própria do Renato com preços COTAHIST + eventos da B3): o job insere a linha de evento sozinho; o Core não muda.
- Importar o xlsx da B3 direto pela tela.
- Tipos de movimentação encontrados e deixados de fora: `Cessão de Direitos` (32), `Rendimento` (975), `Dividendo` (156), `JCP` (256), `Amortização` (2) → Passo 7. `Cisão` (2) e troca de ticker → à mão por enquanto; se repetir, vira tipo próprio.