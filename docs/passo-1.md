# Passo 1 — Contrato

> Este arquivo é o contrato do passo atual. A IA lê **só isto** e o código do `Carteira.Core`.
> A visão do produto e o roadmap ficam fora do repositório, de propósito.

## Objetivo e critério de pronto

**Frase do passo:** "Importo minhas operações do Tesouro Direto de um CSV e abro uma página que mostra cada título que tenho, quanto vale hoje e quanto rendeu."

**Critério de pronto (todos obrigatórios):**

- [ ] Com o túnel SSH aberto, `http://localhost:8081/carteira` abre a página.
- [ ] A página lista minhas posições reais do Tesouro com quantidade, custo, valor de hoje e rentabilidade.
- [ ] O total da carteira bate com o que o site do Tesouro mostra (tolerância: centavos).
- [ ] Os preços foram atualizados automaticamente pelo job, sem eu fazer nada, por pelo menos 2 dias seguidos.
- [ ] Reimportar o mesmo CSV não duplica nada.
- [ ] Todos os testes do núcleo passam com `dotnet test` dentro do container.
- [ ] Subir tudo do zero é `docker compose up -d` e nada mais.

**Prazo:** 14 dias corridos a partir do início. Passou, o escopo estava errado: cortar, não esticar.

## Escopo

**Entra:**

- Ativos da classe Tesouro Direto (Selic, Prefixado, IPCA+, com ou sem juros semestrais, Renda+, Educa+). Todos tratados igual: título com preço unitário diário.
- Operações de **aplicação** e **resgate** (venda antecipada ou vencimento, tratados igual).
- Importação em lote por CSV próprio, idempotente.
- Job diário que busca preços na minha API REST e grava.
- Posição por título e total da carteira: quantidade, custo, valor de mercado, rentabilidade bruta absoluta e percentual.
- Resultado realizado bruto por resgate (para virar base do IR no futuro).
- Uma única página web, somente leitura.
- Tudo em container, acessado por túnel SSH (sem nginx, sem exposição pública).

**Fica de fora (não implementar nem "deixar preparado"):**

- IR, IOF, taxa de custódia B3, taxa da corretora. Rentabilidade é **bruta**.
- Cupons de juros semestrais como evento (v1: se recebi cupom, lanço como resgate parcial de valor equivalente ou ignoro; decidir no passo 2).
- Tela de lançamento manual. V1 muda o CSV e reimporta.
- Gráfico, série histórica, série diária persistida.
- Login, múltiplos usuários, perfis.
- Parser do extrato oficial do Tesouro/B3.
- Qualquer outra classe de ativo.
- Design. A página pode ser uma tabela HTML sem CSS.

## Modelo de domínio

Quatro entidades. Nada mais nasce neste passo.

**Titular** (`Titular`)

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Id` | Guid | gerado |
| `Nome` | string | como aparece na página |
| `Slug` | string | chave natural, único, minúsculo, sem espaço |

**Ativo** (`Asset`)

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Id` | Guid | gerado |
| `Classe` | enum `AssetClass` | v1: só `TesouroDireto` |
| `Codigo` | string | chave natural, único. **É o `codigo` da API de preços**, sem transformação: `tesouro-selic-2029-03-01` |
| `Nome` | string | como aparece no site do Tesouro. Ex.: `Tesouro IPCA+ 2035` |
| `Vencimento` | DateOnly | obrigatório |

**Operação** (`Trade`)

| Campo | Tipo | Regra |
| --- | --- | --- |
| `Id` | Guid | gerado |
| `TitularId` | Guid | FK |
| `AssetId` | Guid | FK |
| `Data` | DateOnly | data de liquidação informada pelo Tesouro |
| `Tipo` | enum `TradeType` | `Aplicacao` ou `Resgate` |
| `Quantidade` | decimal(18,8) | > 0 sempre; o tipo diz o sinal |
| `PrecoUnitario` | decimal(18,6) | > 0; preço por título na data |
| `Taxas` | decimal(18,2) | ≥ 0; v1 sempre 0, campo existe |
| `Moeda` | string(3) | v1: só `BRL` |
| `ChaveImportacao` | string | hash determinístico de (Slug do Titular, Codigo, Data, Tipo, Quantidade, PrecoUnitario); único. Garante idempotência |

**Preço diário** (`DailyPrice`)

| Campo | Tipo | Regra |
| --- | --- | --- |
| `AssetId` | Guid | FK |
| `Data` | DateOnly | chave composta com AssetId |
| `PrecoUnitario` | decimal(18,6) | preço de **resgate** (venda) do dia, não o de compra |

**Invariantes (o núcleo recusa, com exceção de domínio):**

- Resgate com quantidade maior que a posição na data → `InsufficientPositionException`.
- Operação com data posterior ao vencimento do ativo → `TradeAfterMaturityException`.
- Preço diário duplicado para (ativo, data) → substitui, não duplica.
- Quantidade ou preço ≤ 0 → `ArgumentOutOfRangeException`.

**Nota sobre o preço:** o Tesouro publica preço de compra e preço de venda (resgate) para cada título. A carteira vale o que eu receberia se resgatasse hoje, portanto o valor de mercado usa o **preço de venda** — o campo `puVenda` da API.

## Regras de cálculo

Método: **custo médio ponderado** (o mesmo que a Receita exige, então o IR do passo 5 nasce em cima disto). Todo cálculo é uma função pura: recebe lista de operações + preços, devolve números. Precisão total em `decimal` internamente; arredondar para 2 casas **só na exibição**.

**R1 — Aplicação**

    Quantidade_nova = Q + q
    Custo_novo      = C + q × p + taxas
    CustoMédio      = Custo / Quantidade

**R2 — Resgate** (não altera o custo médio)

    CustoBaixado    = q × CustoMédio
    Resultado       = q × p − taxas − CustoBaixado
    Quantidade_nova = Q − q
    Custo_novo      = C − CustoBaixado

**R3 — Posição na data D:** aplicar R1/R2 em ordem cronológica sobre todas as operações com `Data ≤ D`. Empate de data: aplicações antes de resgates. A posição é por par (Ativo, Titular): o mesmo título comprado por titulares diferentes gera posições independentes, cada uma com seu próprio custo médio.

**R4 — Preço na data D:** o `DailyPrice` mais recente com `Data ≤ D`. Sem nenhum → valor de mercado indefinido (a página mostra "sem preço", não zero).

**R5 — Valor de mercado e rentabilidade não realizada**

    Valor = Q × Preço_D
    Rent  = Valor − C
    Rent% = (Valor − C) / C

**R6 — Carteira:** soma de `Custo`, `Valor` e `Rent` de todas as posições com `Quantidade > 0`, de todos os titulares. `Rent%` da carteira = `Rent` total / custo das posições **com preço**, nunca média dos percentuais. Posições sem preço entram no `Custo` total e ficam fora do `Valor`, do `Rent` e da base do `Rent%`, com aviso.

**Exemplo de referência (vira o teste principal)** — Tesouro Selic 2029, taxas = 0:

| Data | Operação | Qtd | Preço unit. (R$) | Qtd após | Custo após (R$) | Custo médio (R$) | Resultado realizado (R$) |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 10/01/2025 | Aplicação | 2,5 | 14.000,00 | 2,5 | 35.000,00 | 14.000,000000 | — |
| 15/03/2025 | Aplicação | 1,0 | 14.300,00 | 3,5 | 49.300,00 | 14.085,714286 | — |
| 20/06/2025 | Resgate | 1,5 | 14.600,00 | 2,0 | 28.171,428571 | 14.085,714286 | 771,43 |

Preço de venda em 26/09/2026: R$ 15.200,00.

- Valor de mercado = 2,0 × 15.200,00 = **30.400,00**
- Rentabilidade não realizada = 30.400,00 − 28.171,43 = **2.228,57** = **7,91%**
- Resultado realizado acumulado = **771,43**

**Com taxas** (mesma primeira aplicação, taxa 10,00): custo = 35.010,00, custo médio = 14.004,00.

**Decisões de implementação** (tomadas ao escrever os testes 1–17):

- `PortfolioSnapshot.Posicoes` contém só posições com `Quantidade > 0`. Posição zerada sai da lista; seu resultado realizado continua em `ResultadoRealizadoTotal`.
- `RentPercentual` (da posição e da carteira) é fração, não percentual: 7,91% = `0,0791…`. Os testes comparam `Math.Round(x × 100, 2)`.
- Carteira vazia (ou custo das posições com preço zero) → `RentPercentual = 0`, sem exceção.
- `RentTotal` soma só o `Rent` das posições com preço; `RentPercentual` da carteira divide por `Custo` dessas mesmas posições (R6). O `CustoTotal` continua incluindo as sem preço.
- Validação de argumentos no início de `Calculate`: quantidade ≤ 0, preço ≤ 0 (`ArgumentOutOfRangeException`) e ativo fora de `assets` (`ArgumentException`) valem para todas as operações, inclusive as com `Data > asOf`. Vencimento (`TradeAfterMaturityException`) e posição (`InsufficientPositionException`) só são checados nas operações com `Data ≤ asOf`.
- Ao zerar a quantidade num resgate, o custo é fixado em 0, para não deixar resíduo de `decimal` do custo médio.
- Valores inventados nos testes, fora do exemplo de referência:
  - T05: resgate total de 2,0 a 15.000,00 em 10/09/2025 → resultado realizado acumulado 2.600,00.
  - T08: aplicação de 2,5 a 14.000,00 e resgate de 1,0 a 14.200,00 no mesmo dia (10/01/2025) → Qtd 1,5; custo 21.000,00; resultado 200,00.
  - T14: IPCA+ 2035 com 3,25 a 3.210,50 (linha do CSV de exemplo) e preço de 3.400,00 em 26/09/2026 → custo total 38.605,55; valor total 41.450,00; rent 2.844,45; 7,37%.

**Decisões de implementação** (tomadas ao escrever os testes 18–30, `CsvTradeParser`):

- **`ChaveImportacao`:** SHA-256 em hexadecimal minúsculo (UTF-8) do texto `codigo|data|tipo|quantidade|preco`, com `data` em `yyyy-MM-dd`, `tipo` como o **nome do enum `TradeType`** (`Aplicacao` ou `Resgate`, não `APLICACAO`/`RESGATE` do CSV), `quantidade` e `preco` em ponto decimal, cultura invariante e sem zeros à direita (`2,50` e `2,5` geram a mesma chave; `0` vira `0`).
- **O texto do tipo na chave não pode ser renomeado depois da T7.** A T7 grava a chave em `trades.import_key`; renomear um membro de `TradeType` (ou mudar qualquer parte do formato acima) muda todas as chaves e a reimportação passa a duplicar. Mudar o formato exige recalcular as chaves gravadas.
- **Assinatura:** `Parse(Stream, DateOnly hoje)`. `hoje` entra por parâmetro para o Core continuar puro; a Web passa a data local (ver "Regras do núcleo").
- **Erros:** o parser reporta todos os problemas de cada linha, um `ParseError(linha, motivo)` por problema. A linha é a posição física no arquivo (cabeçalho = 1, primeira operação = 2). Linhas em branco são ignoradas, mas contam na numeração.
- **Cabeçalho:** tem que ser exatamente `data;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas`. Se não for, o único erro é na linha 1 e o resto do arquivo não é lido. Linha de dados com número de colunas diferente de 8 é erro da linha.
- **Tudo ou nada:** havendo qualquer erro, `Linhas` vem vazia e só `Erros` é preenchida (caso 29).
- **Números:** formato fixo `1234,56` (só dígitos e uma vírgula, sinal `-` opcional para o erro sair como "maior que zero"); ponto decimal e separador de milhar são erro (caso 26). `quantidade` aceita até 8 casas e `preco_unitario` até 6; mais que isso é erro.
- **`taxas`:** vazio = 0; senão qualquer número ≥ 0, **sem limite de casas** no parser (o contrato não define um; o banco é `decimal(18,2)`).
- **`vencimento`:** obrigatório na primeira ocorrência do código; nas seguintes pode vir vazio, e se vier tem que ser igual ao da primeira. Todas as `TradeRow` do mesmo código saem com o mesmo `Vencimento`.
- **`codigo`:** regex `^[a-z0-9-]+$` (minúsculas, dígitos e hífen). Vazio, espaço ou maiúscula é erro (caso 30). O parser não valida o formato do slug além disso.
- **`data`:** `dd/MM/yyyy` exato; posterior a `hoje` é erro (caso 21). `titulo` vazio é erro.

**Decisões de implementação** (tomadas ao adicionar `Titular` — testes 31 e 32):

- **Posição por (Ativo, Titular):** o `PositionCalculator` agrupa operações por par `(AssetId, TitularId)`, não só por `AssetId`. O mesmo título comprado por titulares diferentes gera posições independentes, cada uma com seu próprio custo médio e resultado realizado; a carteira (R6) soma todas.
- **`ChaveImportacao` (formato novo):** SHA-256 em hexadecimal minúsculo (UTF-8) do texto `titular-slug|codigo|data|tipo|quantidade|preco` — o slug do titular entra **antes** do código; o resto da normalização não muda (data `yyyy-MM-dd`, decimais em ponto sem zeros à direita). Isso invalida qualquer chave calculada no formato anterior (sem titular); como a gravação em banco (T7) ainda não existe, não há chave persistida para migrar.
- **`CsvTradeParser`:** a coluna `titular` entra logo depois de `data` (posição 2 de 9). Validada com a mesma regra do `codigo` (slug: minúsculas, dígitos, hífen); vazia, com espaço ou maiúscula é erro (caso 32).
- **`Calculate`:** ganha o parâmetro `titulares` (`IReadOnlyList<Titular>`), antes de `assets`. `Trade` cujo `TitularId` não está em `titulares` → `ArgumentException`, mesma regra já usada para `AssetId` fora de `assets`.
- **`PositionSnapshot`:** ganha `TitularId` e `NomeTitular`.
- **Página:** um bloco por titular, cada um com seu subtotal (soma das posições daquele titular), e o total geral (já existente em `PortfolioSnapshot`) no fim. O agrupamento por titular é feito na Web, filtrando `Posicoes` por `TitularId`; o Core não calcula subtotal por titular.

## CSV de importação

Formato próprio, fixo, montado uma vez a partir do extrato do Tesouro. O parser do extrato oficial é outro passo.

**Formato:** UTF-8, separador `;`, decimal com `,`, data `dd/MM/yyyy`, primeira linha é cabeçalho, uma operação por linha.

```csv
data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas
10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0
15/03/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;1,0;14300,00;0
20/06/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;RESGATE;1,5;14600,00;0
05/02/2025;renato;tesouro-ipca-2035-05-15;Tesouro IPCA+ 2035;15/05/2035;APLICACAO;3,25;3210,50;0
```

**Colunas:**

| Coluna | Regra |
| --- | --- |
| `data` | obrigatória, data válida, não futura |
| `titular` | obrigatória; slug do titular (minúsculas, sem espaço, mesmo padrão do `codigo`). Primeira ocorrência cria o titular |
| `codigo` | obrigatória; o `codigo` da API de preços, copiado de `GET /titulos`. Minúsculas, sem espaço. É a chave do ativo |
| `titulo` | obrigatória; nome para exibição, como no site do Tesouro |
| `vencimento` | obrigatória na primeira ocorrência do código; nas demais, se vier, tem que ser igual |
| `tipo` | `APLICACAO` ou `RESGATE`, sem distinção de caixa |
| `quantidade` | > 0, até 8 casas |
| `preco_unitario` | > 0, até 6 casas |
| `taxas` | ≥ 0; vazio = 0 |

**Comportamento da importação:**

1. Lê o arquivo inteiro e valida todas as linhas antes de gravar qualquer coisa. Uma linha inválida aborta tudo e devolve a lista de erros com número de linha e motivo.
2. Cria os ativos que não existem (por `Codigo`) e os titulares que não existem (por `Slug`).
3. Calcula a `ChaveImportacao` de cada linha. Linha cuja chave já existe no banco é **ignorada silenciosamente** (contabilizada como "já existente"). Isso garante que reimportar o mesmo arquivo não duplica.
4. Ordena por data e valida as invariantes do domínio (resgate sem posição, data após vencimento) sobre o conjunto **existente + novo**. Falhou, aborta tudo.
5. Grava numa transação única.
6. Devolve um resumo: `N importadas, M já existentes, K ativos criados`.

**Como acionar na v1:** um endpoint `POST /api/import` que recebe o arquivo (multipart). Uso via `curl` do meu computador. Sem tela de upload.

## API de preços e job diário

A API de preços é a minha `tesouro-direto` (https://github.com/renatojsilvas/tesouro-direto), já em produção. A Carteira só consome; não a altera, não a embute, não compartilha banco com ela.

**Contrato (conferido no código-fonte em 26/09/2026 — `Program.cs`, `PrecoEndpoints.cs`, `TituloEndpoints.cs`, `PrecoTaxaDto.cs`, `PrecoTaxaDiaDto.cs`, `TituloDto.cs`, `infra/nginx/tesouro-direto.conf`):**

| Item | Valor |
| --- | --- |
| URL base | `https://dadosdotesourodireto.com.br/api/v1` — o nginx corta o `/api/` e a API monta tudo sob `MapGroup("/v1")` |
| Autenticação | header `X-Api-Key` com uma **client key** (`td_…`) gerada em `/desenvolvedores`. Não usar a service key. |
| Limite | 60 req/min por client key (429 + `Retry-After`) |
| JSON | camelCase; datas como string `yyyy-MM-dd`; decimais como número; enums como string |
| Lista de títulos | `GET /titulos` → `[{ codigo, tipoTitulo, dataVencimento, indexador, pagaJurosSemestrais, vencido, _links }]`. **Não há campo `nome`**: o nome de exibição vem do meu CSV |
| Identidade | `codigo` = slug tipo + vencimento completo, ex. `tesouro-selic-2029-03-01` |
| **Fechamento do dia, todos os títulos** | `GET /precos?dataBase=yyyy-MM-dd` → `[{ codigo, dataBase, taxaCompra, taxaVenda, puCompra, puVenda, puBase }]`. Dia sem pregão ou ainda não importado → `200` com lista vazia. `400` se a data for futura. **É o endpoint do job** |
| Preço mais recente de um título | `GET /titulos/{codigo}/preco-atual` → `{ dataBase, taxaCompra, taxaVenda, puCompra, puVenda, puBase }` |
| Histórico de um título | `GET /titulos/{codigo}/precos?dataInicio&dataFim` → array do mesmo DTO |
| Campo usado | **`puVenda`** (nulável: título sem venda naquele dia vem `null` → tratar como "sem preço", não gravar) |
| Data do preço | `dataBase`. **Gravar o `DailyPrice` nessa data, nunca em "hoje"** |
| Atualização | job da API importa o CSV do Tesouro Transparente às 06:00; o dado é do dia útil anterior |

**Decisão:** consumir pela URL pública com client key, não pela rede Docker interna. Desacopla os dois `compose`, não exige `external network`, e é o mesmo caminho que qualquer outro cliente usa. Se um dia a latência incomodar, trocar para `http://app:8080` na rede `tesouro-net` é uma linha de config.

**Job diário (`PriceSyncJob`):**

- `BackgroundService` dentro do container `web`. Nada de cron no host.
- Horário: **07:00 America/Sao_Paulo**, uma hora depois da importação da API. Também roda uma vez ao subir o container.
- Uma única chamada por dia: `GET /precos?dataBase=<ontem>`. Filtra os códigos que tenho com posição > 0 e grava `DailyPrice(ativo, dataBase, puVenda)` para cada um; substitui se já existir. Lista vazia = dia sem pregão, não é erro.
- Backfill: no boot e a cada execução, para cada um dos últimos 7 dias corridos sem nenhum `DailyPrice` gravado, chama `/precos?dataBase=<dia>`. No máximo 7 chamadas; cobre o container ter ficado fora.
- Falha de rede ou 5xx: loga `Warning` e tenta na próxima execução. Não derruba a aplicação.
- 401/403: loga `Error` (chave inválida) e para de tentar até o próximo boot.
- Código meu ausente na resposta ou `puVenda` nulo: loga `Warning` com o código; a página mostra "sem preço".

**Endpoint manual:** `POST /api/prices/sync` na Carteira dispara o job na hora.

## Arquitetura, projetos e regras de código

Três projetos numa solution. **Não criar mais nenhum neste passo.**

| Projeto | Conteúdo | Dependências | Testes |
| --- | --- | --- | --- |
| `Carteira.Core` | entidades, enums, exceções de domínio, `PositionCalculator`, `CsvTradeParser` | nenhuma além da BCL | **sim, 100% das regras** |
| `Carteira.Web` | ASP.NET Core minimal API + Blazor Server (uma página); EF Core + Npgsql; `PriceSyncJob`; `IPriceApiClient` (HttpClient) | Core | não |
| `Carteira.Core.Tests` | xUnit | Core | — |

**Regras do núcleo (`Core`):**

- Zero referências a EF, HTTP, DI, logging, DateTime.Now. Tudo que o núcleo precisa entra por parâmetro.
- `PositionCalculator.Calculate(IReadOnlyList<Titular> titulares, IReadOnlyList<Asset> assets, IReadOnlyList<Trade> trades, IReadOnlyList<DailyPrice> prices, DateOnly asOf)` → `PortfolioSnapshot` (lista de `PositionSnapshot` + totais). Uma função estática. Esta é a assinatura; a IA não inventa outra. Cada `PositionSnapshot` carrega `Codigo` e `Nome` do `Asset` correspondente e `TitularId`/`NomeTitular` do `Titular` correspondente; a posição é por par (`AssetId`, `TitularId`). `Trade` cujo `AssetId` não está em `assets` ou cujo `TitularId` não está em `titulares` → `ArgumentException` (o importador garante que não acontece; não precisa de teste próprio).
- `CsvTradeParser.Parse(Stream, DateOnly hoje)` → `ParseResult` com lista de `TradeRow` válidas ou lista de `ParseError(linha, motivo)`. Não toca banco. `hoje` entra por parâmetro (regra "não futura" da coluna `data`); a Web passa `DateOnly.FromDateTime(DateTime.Now)` com `TZ=America/Sao_Paulo`.
- Records imutáveis para tudo que sai do cálculo.

**Regras da casca (`Web`):**

- EF Core com migrations no próprio projeto; `Database.Migrate()` no startup. Sem repository, sem unit of work, sem CQRS, sem MediatR. `DbContext` direto nos endpoints e no job.
- Blazor Server em modo simples: uma página `/carteira` que chama o `DbContext`, passa para o `PositionCalculator` e renderiza um bloco por titular (tabela de posições daquele titular + subtotal) e o total geral da carteira no fim. Sem componentes reutilizáveis, sem layout, sem CSS além do padrão.
- Três endpoints: `POST /api/import`, `POST /api/prices/sync`, `GET /health`.
- Configuração por variáveis de ambiente: `ConnectionStrings__Default`, `PriceApi__BaseUrl`, `PriceApi__ApiKey`, `PriceSync__HourLocal`.

**Banco (Postgres):** três tabelas, uma por entidade, nomes em snake_case. Índice único em `trades.import_key` e em `assets.code`. Chave composta em `daily_prices(asset_id, date)`.

**Regra de ouro contra o travamento:** neste passo, o código da casca pode ser feio, repetido e sem padrão. Eu não refatoro casca antes do deploy. O único código que precisa ser "do meu jeito" é o `Core`, e ele é pequeno.

## Casos de teste do núcleo

São os testes que importam. A lista é fechada: a IA implementa estes, não inventa outros "para cobertura". Cada linha vira um método `[Fact]` ou uma linha de `[Theory]`.

**`PositionCalculator`**

| # | Caso | Resultado esperado |
| --- | --- | --- |
| 1 | Uma aplicação, sem preço | Qtd e custo corretos; valor de mercado nulo |
| 2 | Duas aplicações a preços diferentes | Custo médio ponderado = 14.085,714286 (exemplo de referência) |
| 3 | Aplicação com taxas | Taxas entram no custo: 35.010,00 / 14.004,00 |
| 4 | Resgate parcial | Qtd 2,0; custo 28.171,428571; custo médio inalterado; resultado 771,43 |
| 5 | Resgate total | Qtd 0; custo 0; posição fora da lista de ativas; resultado realizado mantido |
| 6 | Resgate maior que a posição | `InsufficientPositionException` |
| 7 | Operação após o vencimento | `TradeAfterMaturityException` |
| 8 | Mesma data: aplicação e resgate | Aplicação processada antes do resgate |
| 9 | `asOf` anterior a algumas operações | Só operações com data ≤ asOf entram |
| 10 | Preço exato na data | Usa esse preço |
| 11 | Sem preço na data, há preço anterior | Usa o mais recente anterior |
| 12 | Preço só posterior a `asOf` | Valor de mercado nulo |
| 13 | Exemplo de referência completo | Valor 30.400,00; rent 2.228,57; 7,91% (comparação com 2 casas) |
| 14 | Carteira com dois ativos | Totais = soma; `Rent%` = rent total / custo total |
| 15 | Carteira com um ativo sem preço | Custo total inclui; valor total exclui; flag de aviso ligada |
| 16 | Lista de operações vazia | Snapshot vazio, totais zero, sem exceção |
| 17 | Quantidade ou preço ≤ 0 | `ArgumentOutOfRangeException` |
| 31 | Mesmo título em dois titulares | Duas posições distintas (uma por titular); totais da carteira somam |

**`CsvTradeParser`**

| # | Caso | Resultado esperado |
| --- | --- | --- |
| 18 | Arquivo de exemplo da seção CSV | 4 linhas válidas, 2 códigos distintos |
| 19 | Cabeçalho faltando ou coluna a menos | Erro na linha 1 |
| 20 | Data inválida (`31/02/2025`) | Erro com número da linha |
| 21 | Data futura | Erro |
| 22 | Tipo desconhecido | Erro |
| 23 | `aplicacao` em minúsculas | Aceito |
| 24 | Quantidade `0` ou negativa | Erro |
| 25 | `taxas` vazio | Interpretado como 0 |
| 26 | Decimal com ponto em vez de vírgula | Erro (formato fixo) |
| 27 | Vencimento diferente para o mesmo código em duas linhas | Erro na segunda linha |
| 28 | Duas linhas idênticas | Duas `TradeRow` com a mesma `ChaveImportacao` (a dedup é na gravação, não no parser) |
| 29 | Uma linha inválida no meio | Nenhuma linha válida devolvida; só a lista de erros |
| 30 | `codigo` vazio, com espaço ou com maiúscula | Erro (tem que ser exatamente o slug da API) |
| 32 | `titular` vazio, com espaço ou com maiúscula | Erro |

**Fora dos testes, de propósito:** endpoints, Blazor, EF, job, HttpClient. Se algo ali quebrar, eu vejo na página.

## Containers, acesso e deploy

Dois containers, um `docker compose`, nenhum processo fora do Docker. O nginx da VPS não participa: a Carteira só é acessada por túnel SSH.

**Serviços do `docker-compose.yml`:**

| Serviço | Imagem | Porta | Observação |
| --- | --- | --- | --- |
| `db` | `postgres:17` | só na rede interna | volume nomeado `carteira_pgdata` |
| `web` | build do `Dockerfile` da solution | `127.0.0.1:8081:8080` | só loopback: nada expõe fora da VPS; acesso por túnel SSH |

**Dockerfile multi-stage** (a IA gera; pontos fixos):

1. Estágio `build`: `mcr.microsoft.com/dotnet/sdk:10.0`, `dotnet restore` + `dotnet test Carteira.Core.Tests` + `dotnet publish -c Release`. **Teste falhou, imagem não é gerada.** Isso é o CI da v1.
2. Estágio final: `mcr.microsoft.com/dotnet/aspnet:10.0`, usuário não root, `ASPNETCORE_URLS=http://+:8080`.

**Variáveis de ambiente** (num `.env` fora do git):

```
POSTGRES_PASSWORD=<segredo>
ConnectionStrings__Default=Host=db;Database=carteira;Username=carteira;Password=<segredo>
PriceApi__BaseUrl=https://dadosdotesourodireto.com.br/api/v1
PriceApi__ApiKey=td_<client key gerada em /desenvolvedores>
PriceSync__HourLocal=7
TZ=America/Sao_Paulo
```

**Acesso (sem nginx):** túnel SSH do meu computador direto no container, que só escuta em loopback na VPS:

```
ssh -N -L 8081:127.0.0.1:8081 usuario@vps
```

Com o túnel aberto: `http://localhost:8081/carteira` (página) e `http://localhost:8081/health`. O app roda na raiz, sem prefixo de path, então não há `UsePathBase`. O túnel também serve para os `curl` de `POST /api/import` e `POST /api/prices/sync`. A API de preços é consumida pela URL pública, então não há rede Docker compartilhada a configurar.

**Deploy (processo inteiro):**

```
git pull
docker compose build
docker compose up -d
curl -s http://127.0.0.1:8081/health
```

Sem pipeline, sem registry, sem GitHub Actions neste passo. Build na própria VPS. Automatizar isso é polimento entre passos.

**Backup:** `docker compose exec db pg_dump -U carteira carteira > backup-$(date +%F).sql` uma vez por semana, manual. O CSV original também é backup: com ele eu reconstruo tudo.

## Ordem de execução

Dez tarefas, nesta ordem, uma sessão de IA por tarefa. Cada tarefa termina com algo rodando. A tarefa 1 é o deploy: primeiro coloco no ar, depois preencho.

- [X] **T1 — Esqueleto no ar.** Solution com os 3 projetos vazios, `GET /health` devolvendo `ok`, Dockerfile, compose, `.env`. Critério: `GET /health` responde `ok` via docker compose na porta 8081 (na VPS, e pelo túnel SSH). (1 dia)
- [X] **T2 — Contrato da API de preços.** Gerar uma client key em `/desenvolvedores`. Rodar `curl` em `GET /titulos` e em `GET /titulos/{codigo}/preco-atual` para um título meu; conferir que os campos batem com a tabela de contrato. Sem código. (1 hora)
- [X] **T3 — Entidades.** `Asset`, `Trade`, `DailyPrice`, enums, exceções de domínio. Sem testes ainda: são só records. (2 horas)
- [X] **T4 — `PositionCalculator`.** Testes 1–17 escritos **antes**, a partir das tabelas deste documento; depois a implementação até todos passarem. (2 dias)
- [X] **T5 — `CsvTradeParser`.** Testes 18–30 antes, depois implementação. (1 dia)
- [X] **T6 — Meu CSV real.** Montar o arquivo a partir do extrato do Tesouro, pegando cada `codigo` em `GET /titulos`. Rodar o parser contra ele num teste temporário até passar limpo. Guardar o CSV fora do repositório. (meio dia)
- [X] **T7 — Banco e importação.** EF Core, migration, `POST /api/import`. Critério: importar meu CSV na VPS duas vezes e a segunda devolver `0 importadas`. (1 dia)
- [X] **T8 — Job de preços.** `IPriceApiClient`, `PriceSyncJob`, `POST /api/prices/sync`. Critério: tabela `daily_prices` populada para todos os meus códigos, com a data-base da API. (1 dia)
- [X] **T9 — Página.** `/carteira` renderizando a tabela de posições e o total. Critério: total bate com o site do Tesouro. (1 dia)
- [ ] **T10 — Dois dias de observação.** Não codar. Ver se o job rodou sozinho e os números mudaram. Marcar o critério de pronto. (2 dias)

Soma: ~11 dias. Folga de 3 para o que der errado.

**Modelo por tarefa** (no Claude Code, `/model`):

| Tarefa | Modelo | Por quê |
| --- | --- | --- |
| T1, T3, T5, T7, T8, T9 | Sonnet 5 | trabalho bem especificado; velocidade e custo importam mais que profundidade |
| T4 — escrever os testes 1–17 | Opus 5.5 | os números do exemplo de referência têm que sair exatos do documento; um erro aqui contamina tudo |
| T4 — implementar até passar | Sonnet 5 | loop `dotnet test` → corrigir; não exige raciocínio, exige iteração |
| T2, T6, T10 | nenhum | são `curl`, planilha e observação; IA só atrapalha |
| Contrato do Passo 2, decisão do que polir | Opus 5.5 ou Fable 5.1 | é onde a decisão errada custa semanas |

**Lista de "ideias para depois"** (vazia de propósito; preencher, não executar):

- Taxas de custódia (B3/corretora) e cupons de juros semestrais do extrato: hoje ignorados na importação. Entram no Passo 5 (IR) como despesa dedutível e como rendimento, respectivamente.
- Importação inicial cobre só as posições atuais: títulos encerrados ficaram fora, e cada título pode ter uma única linha APLICACAO com a quantidade e o preço médio do extrato de posição, na data da primeira aplicação. Histórico completo: importar depois, se fizer falta.