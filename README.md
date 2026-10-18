# meus-investimentos

## Uso

### Subir e acessar

Dois containers (`db` e `web`) num `docker compose`. O `web` só escuta em loopback na VPS
(`127.0.0.1:8081`); não há nginx nem exposição pública. As variáveis ficam num `.env` fora do
git (`POSTGRES_PASSWORD`, `ConnectionStrings__Default`, `PriceApi__BaseUrl`, `PriceApi__ApiKey`,
`PriceSync__HourLocal`, `TZ`).

Deploy na VPS:

```
git pull
docker compose build
docker compose up -d
curl -s http://127.0.0.1:8081/health
```

Acesso do computador local, por túnel SSH:

```
ssh -N -L 8081:127.0.0.1:8081 usuario@vps
```

Com o túnel aberto:

- `http://localhost:8081/carteira`: posições, valores e curvas por titular e consolidado.
- `http://localhost:8081/lancar`: formulário de operação e bloco "Atualizar PU".
- `http://localhost:8081/health`: responde `ok`.

Os `curl` abaixo usam `http://localhost:8081` e também passam pelo túnel.

### Importar operações

`POST /api/import` recebe um CSV multipart, separado por `;`, datas `dd/MM/yyyy` e vírgula
decimal. O cabeçalho aceita 9 ou 10 colunas.

**9 colunas** (Tesouro Direto), de `tests/e2e/operacoes.csv`:

```csv
data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas
10/01/2025;renato;tesouro-selic-2029-03-01;Tesouro Selic 2029;01/03/2029;APLICACAO;2,5;14000,00;0
```

**10 colunas**, com `classe` no fim (`TESOURO`, `RENDA_FIXA` ou vazio, que vale `TESOURO`; sem
distinção de caixa), de `docs/passo-5.md`:

```csv
data;titular;codigo;titulo;vencimento;tipo;quantidade;preco_unitario;taxas;classe
10/01/2025;renato;cdb-inter-2027-03;CDB Inter 2027;01/03/2027;APLICACAO;10;1000,00;0;RENDA_FIXA
```

`vencimento` vazio só é aceito na primeira ocorrência de um código se a classe for `RENDA_FIXA`
(ativo sem prazo, como o cofrinho). Ativo que já existe com outra classe é erro da linha.

```
curl -s -F "arquivo=@operacoes.csv;type=text/csv" http://localhost:8081/api/import
```

Resposta: `{ "importadas", "jaExistentes", "ativosCriados", "titularesCriados" }`. Reimportar o
mesmo arquivo devolve `importadas: 0`. Erro de linha ou de invariante devolve `400`.

### Renda fixa

O preço de um ativo `RendaFixa` vem da tela, não da API de preços.

- **CDB, LCI e similares:** quantidade real de unidades e PU da unidade, como a corretora mostra
  (10 unidades de CDB a PU 1.000,00 na emissão; hoje PU 1.047,32). Para lançar pelo valor, use
  quantidade 1 e PU = valor total.
- **Cofrinho e conta remunerada (sem vencimento):** quantidade = valor em reais e PU = 1,00 em
  todo aporte e resgate. O PU atual é o saldo ÷ cotas (aportes − resgates). Lançar quantidade 1
  por aporte está errado quando há mais de um aporte.
- O `codigo` é um slug inventado (`^[a-z0-9-]+$`), por exemplo `cdb-inter-2027-03`. Faz parte da
  chave de importação: não renomeie depois de importado.
- Ao importar uma APLICACAO de ativo `RendaFixa` sem nenhum PU com data ≤ a da operação, a
  importação grava o PU da operação como PU de emissão.

**Atualizar o PU** (rotina, sempre que quiser refletir o valor atual): em `/lancar`, bloco
"Atualizar PU" (ativo, PU, data; a data não pode ser futura), ou por curl:

```
curl -s -X POST -H "Content-Type: application/json" \
  -d '{"codigo":"cdb-inter-2027-03","data":"30/06/2025","pu":"1047,32"}' \
  http://localhost:8081/api/precos
```

Resposta: `{ "codigo", "data", "pu" }`. Entre duas atualizações o PU fica constante. Um PU para
(ativo, data) já existente substitui o anterior. Código inexistente, data inválida ou futura e
PU ≤ 0 devolvem `400 { "erro" }`.

### Demais endpoints

Lançar uma operação (mesmas regras do CSV; campos em texto, `classe` opcional):

```
curl -s -X POST -H "Content-Type: application/json" \
  -d '{"data":"10/01/2025","titular":"renato","codigo":"cdb-inter-2027-03","titulo":"CDB Inter 2027","vencimento":"01/03/2027","tipo":"APLICACAO","quantidade":"10","preco_unitario":"1000,00","taxas":"0","classe":"RENDA_FIXA"}' \
  http://localhost:8081/api/operacoes
```

Sincronizar agora os preços do Tesouro Direto (o job já roda sozinho uma vez por dia):

```
curl -s -X POST http://localhost:8081/api/prices/sync
```

Backfill dos preços históricos do Tesouro Direto, `{ "ativos", "precosGravados", "dias" }`:

```
curl -s -X POST http://localhost:8081/api/prices/backfill
```

Reconstruir todos os snapshots diários:

```
curl -s -X POST http://localhost:8081/api/snapshots/rebuild
```

Backfill do CDI, `{ "indice", "registros", "de", "ate" }` (`502` se o BCB responder algo inválido):

```
curl -s -X POST http://localhost:8081/api/indices/backfill
```

### Corrigir uma carga errada

Reimportar por cima **não corrige**: a `ChaveImportacao` é o hash de
`titular|codigo|data|tipo|quantidade|preco`. Se a quantidade (ou o preço, a data, o tipo) do CSV
mudou, a chave é nova e a linha entra como operação adicional, ao lado da errada, que continua
no banco.

Para corrigir, apague o ativo errado (operações, preços e o próprio ativo, por `code`) numa
transação, reconstrua os snapshots e reimporte o CSV certo. Troque `<codigo>`:

```
docker compose exec -T db psql -U carteira carteira <<'SQL'
BEGIN;
DELETE FROM trades WHERE asset_id = (SELECT id FROM assets WHERE code = '<codigo>');
DELETE FROM daily_prices WHERE asset_id = (SELECT id FROM assets WHERE code = '<codigo>');
DELETE FROM assets WHERE code = '<codigo>';
COMMIT;
SQL
curl -s -X POST http://localhost:8081/api/snapshots/rebuild
curl -s -F "arquivo=@operacoes.csv;type=text/csv" http://localhost:8081/api/import
```

O ativo é apagado por último, depois das operações e dos preços que apontam para ele. Depois do
`rebuild`, reimportar o CSV certo recria o ativo e as operações (para renda fixa, a importação
volta a gravar o PU de emissão; PUs lançados à mão para esse código precisam ser relançados).
Antes de apagar, faça o backup: `docker compose exec db pg_dump -U carteira carteira > backup-$(date +%F).sql`.

## Teste de ponta a ponta (caixa-preta)

```
docker compose -f compose.test.yml up --build --exit-code-from tests
docker compose -f compose.test.yml down -v
```

Sobe `db` (Postgres vazio), `fakeapi` (nginx reproduzindo a fixture real de preços em
`tests/e2e/fixtures/precos-2026-09-25.http`) e `web` (a mesma imagem do `Dockerfile`); o
serviço `tests` roda `tests/e2e/run.sh`, que importa `tests/e2e/operacoes.csv`, reimporta,
sincroniza os preços e confere os totais em `GET /carteira`. Sai com código ≠ 0 em qualquer
falha.

### Rodar automaticamente antes do push

```
git config core.hooksPath .githooks
```

Uma vez por clone. Depois disso, `git push` roda o teste e2e acima (`.githooks/pre-push`) e
bloqueia o push se ele falhar. Não roda no commit, só no push.
