# Carteira

Aplicação web pessoal para acompanhar investimentos. Stack: .NET 10, Postgres, Blazor Server, Docker.

O que construir agora está em **`docs/passo-4.md`**. Leia-o antes de qualquer tarefa. Ele é o contrato vigente: entidades, regras de cálculo com exemplos numéricos, casos de teste, arquitetura e a lista de tarefas. `docs/passo-1.md` a `docs/passo-3.md` são históricos, válidos onde o novo não os altera. Nada fora do contrato vigente entra neste passo.

## Como trabalhar

- Uma tarefa por sessão. O pedido chega como "implemente T4 conforme o contrato". Faça exatamente isso e pare.
- Antes de codar, leia a seção do contrato que a tarefa cita e o código atual de `Carteira.Core`.
- Terminou a tarefa quando o critério dela (no contrato) é verificável. Diga qual comando ou verificação prova isso.

## Regras de código

- Três projetos: `Carteira.Core`, `Carteira.Web`, `Carteira.Core.Tests`. Não crie outros.
- `Carteira.Core` é puro: sem EF, HTTP, DI, logging, `DateTime.Now`. Tudo entra por parâmetro. Records imutáveis.
- Assinaturas fixas do contrato: `PositionCalculator.Calculate(titulares, ativos, operacoes, precos, dataReferencia)`, `CsvTradeParser.Parse(stream, hoje)`, `DailySeries.Build` e `BenchmarkSeries.Build`. Não invente outras.
- `Carteira.Web`, incluindo `Importacao`, é casca descartável: `DbContext` direto, sem interface nem repository, unit of work, CQRS ou MediatR. Pode ser feio e repetido.
- Dinheiro e quantidade são `decimal`; datas de negócio são `DateOnly`; arredonda só na exibição.
- Tudo roda em container. Nunca sugira `dotnet run` como forma de rodar a aplicação.

## Testes

- Os únicos testes são os casos numerados no contrato vigente (1–48). Implemente esses, não adicione outros "para cobertura".
- Nas tarefas que tocam o Core, o fluxo é: escreva os testes primeiro, depois a implementação, depois `dotnet test` em loop até passar.
- **Nunca altere, apague ou enfraqueça um teste para fazê-lo passar.** Se um teste parece errado, pare e diga qual e por quê.
- Web, EF, job e endpoints não têm teste. Não crie.

## O que não fazer

- Não proponha abstrações, camadas, interfaces, pacotes ou padrões que não estão no contrato. Se achar que falta algo, diga em uma linha e siga sem ele.
- Não refatore código existente no meio de uma tarefa. Não "melhore" nem "organize" o que não foi pedido.
- Não prepare o código para classes de ativo, IR, gráficos ou qualquer coisa fora do passo atual.
- Não adicione observabilidade, resiliência, cache, rate limit, autenticação ou CI além do que o contrato descreve.
- Nunca reescreva histórico já enviado (rebase, amend, force-push) sem confirmação explícita minha na mesma sessão.
- Commits sem trailer, sem Co-Authored-By e sem qualquer linha de atribuição à IA. Autor e mensagem são meus; a mensagem é uma linha curta no imperativo, em português.

## Comandos

```
dotnet test tests/Carteira.Core.Tests    # dentro do container de build
docker compose build && docker compose up -d
curl -s http://127.0.0.1:8081/health
```