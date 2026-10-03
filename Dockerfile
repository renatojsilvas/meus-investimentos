FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Carteira.sln ./
COPY src/Carteira.Core/Carteira.Core.csproj src/Carteira.Core/
COPY src/Carteira.Web/Carteira.Web.csproj src/Carteira.Web/
COPY src/Carteira.Core.Tests/Carteira.Core.Tests.csproj src/Carteira.Core.Tests/
RUN dotnet restore

COPY . .
# Teste falhou, imagem não é gerada.
RUN dotnet test src/Carteira.Core.Tests --no-restore
RUN dotnet publish src/Carteira.Web -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
USER app
ENTRYPOINT ["dotnet", "Carteira.Web.dll"]
