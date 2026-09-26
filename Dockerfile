FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Carteira.sln ./
COPY Carteira.Core/Carteira.Core.csproj Carteira.Core/
COPY Carteira.Web/Carteira.Web.csproj Carteira.Web/
COPY Carteira.Core.Tests/Carteira.Core.Tests.csproj Carteira.Core.Tests/
RUN dotnet restore

COPY . .
# Teste falhou, imagem não é gerada.
RUN dotnet test Carteira.Core.Tests --no-restore
RUN dotnet publish Carteira.Web -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://+:8080
USER app
ENTRYPOINT ["dotnet", "Carteira.Web.dll"]
