FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia tudo antes do restore: restaurar só com os .csproj (sem o resto do
# código) deixa o manifesto de Static Web Assets do publish sem o
# _framework/blazor.web.js, e o Blazor Server fica sem JS (/lancar quebra).
COPY . .
RUN dotnet restore

# Teste falhou, imagem não é gerada.
RUN dotnet test tests/Carteira.Core.Tests --no-restore
RUN dotnet publish src/Carteira.Web -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
USER app
ENTRYPOINT ["dotnet", "Carteira.Web.dll"]
