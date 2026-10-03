# Passo 2 — Curva da carteira

> Contrato do passo atual. A IA lê só isto, o `CLAUDE.md` e o código. O `docs/passo-1.md` é histórico e continua valendo onde este não o altera.

## Objetivo e critério de pronto

**Frase do passo:** "Abro a página e vejo a curva do valor da carteira desde a primeira aplicação, por titular e total."

**Critério de pronto (todos obrigatórios):**

- [ ] `daily_prices` tem preço para cada título meu em todo dia útil desde a primeira aplicação dele, carregado uma vez pelo backfill.
- [ ] `daily_snapshots` tem uma linha por (dia útil, titular) desde a primeira aplicação de cada um, preenchida pelo job sem eu fazer nada.
- [ ] A página mostra um gráfico de linha com o valor total e uma linha por titular, do início até hoje.
- [ ] O último ponto da curva é igual ao total geral da tabela, centavo a centavo.
- [ ] Depois de reimportar o CSV com uma operação antiga nova, a curva reflete a mudança sem intervenção manual.
- [ ] Job rodou sozinho por 2 dias seguidos e a curva ganhou 2 pontos.
- [ ] Core: todos os testes (1–40) verdes. E2e verde com o cenário de histórico.
- [ ] Backfill do job diário corrigido para (ativo, dia).

**Prazo:** 3 semanas corridas a 1h/dia. Passou, o escopo estava errado: cortar, não esticar.

## Escopo

**Entra:**

- Backfill histórico de preços: para cada ativo com operação, `GET /titulos/{codigo}/precos?dataInicio&dataFim` da data da primeira operação até hoje. Disparado uma vez por endpoint; depois o job diário mantém.
- Correção do backfill do job diário: por (ativo, dia), não por dia.
- `DailySeries.Build` no Core: função pura que gera a série diária por titular a partir de operações e preços, reaproveitando `PositionCalculator.Calculate`.
- Tabela `daily_snapshots`, preenchida pelo job só para os dias que faltam, invalidada pela importação quando entra operação com data antiga.
- Gráfico de linha na página `/carteira`: SVG gerado no servidor, sem biblioteca. Uma linha por titular e uma para o total.
- Endpoint `POST /api/prices/backfill` (carga inicial) e `POST /api/snapshots/rebuild` (recalcular tudo).
- E2e: um cenário novo com fixture de histórico.

**Fica de fora (não implementar nem "deixar preparado"):**

- Tela de lançamento manual (Passo 3).
- Zoom, seleção de período, tooltip, legenda interativa. O gráfico é estático.
- Rentabilidade por período (TWR, MWR), comparação com CDI ou IPCA, rentabilidade no mês/ano.
- Série por título (só por titular e total).
- Biblioteca de gráfico, JavaScript, CSS.
- Qualquer outra classe de ativo.
- Cupom, custódia, IR.

## Mudanças no domínio

Uma entidade nova. `Titular`, `Ativo`, `Operacao` e `PrecoDiario` não mudam. Convenção de nomes após o polimento: entidades, records e campos em português; as classes de cálculo (`PositionCalculator`, `CsvTradeParser`) mantêm o nome em inglês, e a nova segue o mesmo padrão.

**Snapshot diário** (`SnapshotDiario`) — um por (dia, titular). É cache: tudo nele é recalculável a partir de `Operacao` + `PrecoDiario`, e pode ser apagado e refeito a qualquer momento.

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Data` | DateOnly | chave composta com TitularId |
| `TitularId` | Guid | FK |
| `Custo` | decimal(18,6) | custo total das posições do titular no dia (inclui sem preço) |
| `CustoComPreco` | decimal(18,6) | custo só das posições com preço no dia (base da Rentabilidade%) |
| `Valor` | decimal(18,6) | valor de mercado das posições com preço |
| `Rentabilidade` | decimal(18,6) | `Valor − CustoComPreco` |
| `ResultadoRealizado` | decimal(18,6) | resultado realizado acumulado até o dia |
| `TemPosicaoSemPreco` | bool | aviso do dia |

Não existe linha de "total": o total de um dia é a soma das linhas dos titulares naquele dia, e `Rentabilidade%` do total = `ΣRentabilidade / ΣCustoComPreco`. A página soma; o banco não guarda.

**No Core**, o que sai de `DailySeries.Build` é um record imutável `PontoSerieDiaria` com os mesmos campos, mais `Slug` e `NomeTitular`. A entidade de banco `SnapshotDiario` é cópia dele sem esses dois; a Web converte.

**Invariantes:**

- Só existe snapshot em dia com pelo menos um `PrecoDiario` de algum ativo em que o titular tem posição no fim do dia, ou em dia com operação do titular (ver R7).
- Só existe snapshot a partir da primeira operação do titular.
- Snapshot duplicado para (dia, titular) → substitui.

**Banco:** tabela `daily_snapshots`, chave primária composta `(date, titular_id)`. Índice em `titular_id, date` para a leitura da página.

## Regras da série diária

A série é `Calculate` repetido, um dia por vez. Nenhuma regra de cálculo nova: R1 a R6 do Passo 1 continuam valendo. O que este passo define é **quais dias** entram e **o que se guarda** de cada um.

**Assinatura (fixa):**

`DailySeries.Build(IReadOnlyList<Titular> titulares, IReadOnlyList<Ativo> ativos, IReadOnlyList<Operacao> operacoes, IReadOnlyList<PrecoDiario> precos, DateOnly de, DateOnly ate)` → `IReadOnlyList<PontoSerieDiaria>`, ordenada por (`Data`, `Slug`).

**R7 — Dias da série.** Para um titular, entram os dias `D` com `de ≤ D ≤ ate` tais que: (a) `D ≥` data da primeira operação do titular, e (b) existe `PrecoDiario` em `D` para algum ativo em que o titular tem posição > 0 no fim de `D`, OU o titular tem operação em `D`. Dias sem pregão (fim de semana, feriado) ficam fora: a curva pula, não repete. Cada titular tem sua própria data de início; o total, na página, começa na mais antiga.

**R8 — Ponto do dia.** Para cada dia que entra, `Calculate(…, dataReferencia = D)` filtrado pelo titular: `Custo`, `CustoComPreco`, `Valor`, `Rentabilidade`, `ResultadoRealizado`, `TemPosicaoSemPreco` saem direto do snapshot. Preço do dia segue R4 (o mais recente `≤ D`), então um ativo sem preço em `D` mas com preço anterior usa o anterior.

**R9 — Depois do resgate total.** No dia do resgate total o ponto existe, com `Custo` 0, `Valor` 0, `CustoComPreco` 0 e `ResultadoRealizado` acumulado incluindo esse resgate. Nos dias seguintes, sem posição e sem operação, não há ponto: a série para. Se o titular voltar a aplicar, retoma. A página mostra o buraco como interrupção da linha, não como zero.

**Exemplo de referência (vira o teste 33)** — as mesmas três operações do Passo 1, titular `renato`, Tesouro Selic 2029, com preços em três dias:

| Dia | Preço de venda | Qtd | Custo | Valor | Rentabilidade | Rentabilidade% | ResultadoRealizado |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 10/01/2025 | 14.000,00 | 2,5 | 35.000,00 | 35.000,00 | 0,00 | 0,00% | 0,00 |
| 15/03/2025 | 14.250,00 | 3,5 | 49.300,00 | 49.875,00 | 575,00 | 1,17% | 0,00 |
| 20/06/2025 | 14.600,00 | 2,0 | 28.171,43 | 29.200,00 | 1.028,57 | 3,65% | 771,43 |

`Build(…, de = 01/01/2025, ate = 30/06/2025)` devolve exatamente esses três pontos. Dias entre eles não têm `PrecoDiario`, logo não entram. `CustoComPreco` = `Custo` nos três, porque o único ativo tem preço.

**Decisão sobre custo:** o `Build` chama `Calculate` uma vez por dia, com todas as operações, e filtra o snapshot por titular. Para ~500 dias úteis e ~100 operações isso é instantâneo; não otimizar. Se um dia ficar lento, é tarefa de outro passo, com medida antes.

## Backfill histórico e job

**Endpoint da API usado:** `GET /titulos/{codigo}/precos?dataInicio=yyyy-MM-dd&dataFim=yyyy-MM-dd`, mesmo DTO do `/precos` (`dataBase`, `puVenda`…). O contrato do Passo 1 diz que ele pagina por `Link` + `X-Total-Count` **se** `page`/`pageSize` forem passados; **a T1 deste passo confirma com `curl` o que acontece sem esses parâmetros** (tudo de uma vez ou página padrão) e grava uma fixture de histórico real para o e2e. Só depois disso a T3 é escrita.

Conferido na T1 (03/10/2026, fixtures `historico-selic-2029.http` e `historico-ipca-2035.http`): não há paginação sem `page`/`pageSize` — seis meses vieram inteiros (122 registros) e cinco anos também (933), `x-total-count` igual ao tamanho do corpo, sem header `Link`. O registro NÃO traz `codigo`: o backfill associa a resposta ao ativo pela URL que chamou. Headers iguais aos de `/precos`. O histórico começa no primeiro dia de negociação do título, que é sempre anterior à primeira aplicação nele.

**`POST /api/prices/backfill`** (carga inicial, manual):

1. Para cada ativo com pelo menos uma operação: `dataInicio` = data da primeira operação daquele ativo (de qualquer titular), `dataFim` = hoje.
2. Uma chamada por ativo (ou as páginas que a T1 mostrar necessárias). Grava `PrecoDiario(ativo, dataBase, puVenda)` para cada registro com `puVenda` não nulo; substitui os existentes.
3. Respeita o limite de 60 req/min: com 7 títulos não chega perto; mesmo assim, 429 → espera `Retry-After` e repete uma vez.
4. Devolve `{ ativos, precosGravados, dias }` em JSON.
5. Ao terminar, apaga todos os `daily_snapshots` e dispara o preenchimento (abaixo).

**Correção do job diário (`PriceSyncJob`):** o backfill de 7 dias passa a verificar falta por **(ativo, dia)**: para cada ativo com posição > 0 e cada um dos últimos 7 dias, se não há `PrecoDiario` desse ativo nesse dia, o dia entra na lista a buscar. Continua uma chamada por dia em `/precos?dataBase=`, no máximo 7. Título novo importado hoje recebe a semana anterior no próximo ciclo.

**Preenchimento dos snapshots (mesmo job, logo após os preços):**

1. `de` = menor data de operação; `ate` = hoje.
2. Lê titulares, ativos, operações e preços; chama `DailySeries.Build(…, de, ate)`.
3. Grava só os pontos cujo (dia, titular) ainda não existe em `daily_snapshots`. Nunca sobrescreve em operação normal.
4. Um snapshot que existe no banco mas que o `Build` não devolveu (dia que deixou de ter preço) é deixado como está; só o rebuild limpa.

**Invalidação pela importação:** ao final de `POST /api/import` com `importadas > 0`, apaga os `daily_snapshots` com `date ≥` menor data entre as operações importadas, para todos os titulares, e dispara o preenchimento. Reimportação sem nada novo não apaga nada.

**`POST /api/snapshots/rebuild`:** apaga todos os `daily_snapshots` e dispara o preenchimento. É a saída para qualquer inconsistência: o cache nunca é fonte de verdade.

## Gráfico e página

A página `/carteira` ganha um bloco **no topo**, antes das tabelas: o gráfico. O resto da página não muda.

**O que o gráfico mostra:**

- Eixo X: tempo, da primeira data em `daily_snapshots` até a última. Eixo Y: valor em R$, de zero ao maior total.
- Uma linha para o **total** (soma dos titulares por dia) e uma linha por **titular**. Legenda estática com o nome e a cor.
- Linha de um titular começa no primeiro snapshot dele e interrompe onde não há ponto (R9).
- Abaixo do gráfico, uma linha de texto: "Valor hoje R$ X · Custo R$ Y · Rentabilidade R$ Z (W%)", igual ao total geral da tabela.

**Como é feito:** SVG escrito pelo Blazor no servidor, inline no HTML. Um `<polyline>` por série, eixos como `<line>`, quatro ou cinco rótulos de valor no Y e de data no X como `<text>`. Largura 100%, altura fixa. Sem JavaScript, sem biblioteca, sem CSS além do `fill`/`stroke` inline. Cores: três ou quatro fixas no código.

**Leitura:** a página lê `daily_snapshots` direto do `DbContext`, agrupa por data para o total, e desenha. Não chama `Build` na página: a série vem do cache.

**Fora, de propósito:** tooltip, zoom, seleção de período, escala logarítmica, linha de custo, marcadores de operação. Cada um é uma ideia para depois.

## Casos de teste

Lista fechada. Os testes 1–32 do Passo 1 não mudam. Os novos são só de `DailySeries.Build`; backfill, job, endpoints e gráfico ficam fora, cobertos pelo e2e e pela observação.

**`DailySeries.Build`**

| # | Caso | Resultado esperado |
| --- | --- | --- |
| 33 | Exemplo de referência (3 operações, 3 preços) | Exatamente 3 pontos com os valores da tabela da seção "Regras" (comparação com 2 casas) |
| 34 | Dia dentro do intervalo sem `PrecoDiario` de nenhum ativo | Não gera ponto (a curva pula) |
| 35 | Preço em dia anterior à primeira operação do titular | Não gera ponto para esse titular |
| 36 | Dois titulares com primeiras operações em datas diferentes | Cada série começa na sua data; no dia em que só um tem posição, só ele tem ponto |
| 37 | Dia com preço para um ativo do titular e não para outro (sem preço anterior) | Ponto gerado; `Custo` inclui os dois, `Valor` e `CustoComPreco` só o com preço; `TemPosicaoSemPreco` ligado |
| 38 | Ativo sem preço no dia, mas com preço em dia anterior | Usa o anterior (R4); `TemPosicaoSemPreco` desligado |
| 39 | Resgate total no meio do intervalo, preços continuam depois | Último ponto é o dia do resgate: `Custo` 0, `Valor` 0, `ResultadoRealizado` 2.600,00; o dia seguinte com preço não gera ponto |
| 40 | `de > ate`, ou lista de operações vazia | Lista vazia, sem exceção |

**Decisões de implementação** (tomadas ao escrever os testes 33–40):

- `PontoSerieDiaria(Data, TitularId, Slug, NomeTitular, Custo, CustoComPreco, Valor, Rentabilidade, ResultadoRealizado, TemPosicaoSemPreco)`. Não tem campo de `Rentabilidade%`: os testes calculam `Rentabilidade / CustoComPreco` e comparam `Math.Round(x × 100, 2)`, como no Passo 1.
- R7(b) e R9 revistos ao escrever o 39: o dia com operação do titular entra mesmo sem preço, e o dia do resgate total gera ponto zerado com o resultado realizado acumulado. A redação anterior fazia a série parar no pregão anterior ao resgate, e o resultado do resgate nunca aparecia.
- `ResultadoRealizado` do ponto é do titular, incluindo posições já zeradas. O `CarteiraSnapshot` não expõe isso por titular (posição zerada sai de `Posicoes`, e `ResultadoRealizadoTotal` soma todos os titulares). Por isso o "filtrado pelo titular" do R8 pode exigir filtrar as operações, e não só o snapshot.
- Valores com meio centavo exato (37, 38) são comparados sem arredondar, porque o `Math.Round` padrão é bancário.
- Valores inventados nos testes, fora do exemplo de referência (titular `renato`, taxas 0, salvo indicação):
  - T34: Selic 2,5 a 14.000,00 em 10/01/2025; preços em 10/01 (14.000,00) e 14/01 (14.020,00); `de` 10/01, `ate` 14/01 → pontos só em 10/01 e 14/01.
  - T35: mesma aplicação; preços em 09/01 (13.990,00) e 10/01 (14.000,00); `de` 01/01, `ate` 31/01 → um ponto, 10/01.
  - T36: `renato` Selic 2,5 a 14.000,00 em 10/01/2025; `maria` Selic 1,0 a 14.050,00 em 15/01/2025; preços 10/01 14.000,00, 15/01 14.050,00, 20/01 14.100,00 → 10/01 só `renato` (custo 35.000,00, valor 35.000,00); 15/01 `maria` 14.050,00/14.050,00 e `renato` 35.000,00/35.125,00; 20/01 `maria` 14.050,00/14.100,00 (rent 50,00) e `renato` 35.000,00/35.250,00 (rent 250,00). Ordem por (`Data`, `Slug`).
  - T37: Selic 2,5 a 14.000,00 em 10/01/2025 + IPCA+ 2035 3,25 a 3.210,50 em 05/02/2025; único preço: Selic 14.100,00 em 10/02/2025; `de` = `ate` = 10/02 → custo 45.434,125; custo com preço 35.000,00; valor 35.250,00; rent 250,00; aviso ligado.
  - T38: igual ao 37, mais IPCA 3.220,00 em 07/02/2025 → custo e custo com preço 45.434,125; valor 45.715,00; rent 280,875; aviso desligado.
  - T39: exemplo de referência + resgate total de 2,0 a 15.000,00 em 10/09/2025 (o mesmo do T05); preços da referência + 10/09 (15.000,00) e 11/09 (15.010,00); `de` 01/01, `ate` 30/09 → pontos 10/01, 15/03, 20/06 e 10/09; o último com custo 0, custo com preço 0, valor 0, resultado realizado 2.600,00; 11/09 sem ponto.
  - T40: referência com `de` 30/06 e `ate` 01/01; e operações vazias com `de` 01/01 e `ate` 30/06 → as duas listas vazias.

**E2e — cenário novo em `run.sh`:**

- `fakeapi` passa a servir também `/api/v1/titulos/{codigo}/precos` com a fixture de histórico gravada na T1 (resposta real, byte a byte, headers inclusos), para os dois códigos do CSV de exemplo.
- Depois do fluxo atual (import, reimport, sync): `POST /api/prices/backfill` → confere `precosGravados` igual ao número de registros com `puVenda` na fixture; `GET /carteira` contém um `<svg>` com um `<polyline>` por titular mais um do total, e o texto "Valor hoje" com o mesmo total da tabela.
- Reimporta um CSV com uma operação antiga a mais (data anterior ao último snapshot) → `importadas=1` e a página mostra o total atualizado tanto na tabela quanto na linha "Valor hoje" (invalidação funcionou).
- Fixture do histórico com 3 datas ou mais, para a curva ter pelo menos 3 pontos por titular.

## Tarefas, modelos e prazo

Sete tarefas, uma sessão cada. A T1 é manual e vem antes de qualquer código, porque o formato real do histórico decide a T3 e o e2e.

- [x] **T1 — Contrato do histórico.** `curl -i` em `/titulos/tesouro-selic-2029-03-01/precos?dataInicio=2025-01-01&dataFim=2025-06-30` e no IPCA+ 2035; gravar as duas respostas em `tests/e2e/fixtures/`. Conferir: pagina sem `page`? quantos registros? Anotar na seção "Backfill". Sem código. (30 min)
- [x] **T2 — Backfill do job por (ativo, dia).** Corrigir `PriceSyncJob`. Critério: e2e continua verde. Commit. (1 sessão)
- [ ] **T3 — `DailySeries.Build`.** Testes 33–40 antes, implementação depois. Critério: 1–40 verdes. (2 sessões)
- [ ] **T4 — `daily_snapshots` + preenchimento + invalidação.** Migration, preenchimento no job, invalidação na importação, `POST /api/snapshots/rebuild`. Critério: na VPS, após rebuild, `select count(*) from daily_snapshots` por titular bate com os dias úteis desde a primeira aplicação de cada um. (2 sessões)
- [ ] **T5 — `POST /api/prices/backfill`.** Critério: na VPS, `daily_prices` tem linhas desde a primeira aplicação de cada título; rebuild depois. (1 sessão)
- [ ] **T6 — Gráfico.** SVG na página. Critério: último ponto = total geral da tabela, centavo a centavo; cada titular com sua linha. (2 sessões)
- [ ] **T7 — E2e + observação.** Cenário novo verde; depois 2 dias sem codar vendo a curva ganhar pontos. Marcar o critério de pronto. (1 sessão + 2 dias)

Soma: ~9 sessões de 1h, mais 2 dias de observação. Prazo de 3 semanas tem folga.

**Modelo por tarefa:**

| Tarefa | Modelo | Por quê |
| --- | --- | --- |
| T1 | nenhum | `curl` e leitura |
| T2, T4, T5, T6, T7 | Sonnet 5 | bem especificado; iteração |
| T3 — escrever os testes 33–40 | Opus 5.5 | os números da série têm que sair exatos |
| T3 — implementar | Sonnet 5 | loop `dotnet test` |

**Lista de "ideias para depois"** (preencher, não executar):

- Tela de lançamento manual (Passo 3).
- Gráfico: tooltip, período, linha de custo, marcadores de operação.
- Rentabilidade por período e comparação com CDI/IPCA.