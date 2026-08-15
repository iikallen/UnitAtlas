FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY UnitAtlas.slnx .
COPY src/UnitAtlas.Api/UnitAtlas.Api.csproj src/UnitAtlas.Api/
COPY src/UnitAtlas.Application/UnitAtlas.Application.csproj src/UnitAtlas.Application/
COPY src/UnitAtlas.Contracts/UnitAtlas.Contracts.csproj src/UnitAtlas.Contracts/
COPY src/UnitAtlas.Domain/UnitAtlas.Domain.csproj src/UnitAtlas.Domain/
COPY src/UnitAtlas.Infrastructure/UnitAtlas.Infrastructure.csproj src/UnitAtlas.Infrastructure/
RUN dotnet restore src/UnitAtlas.Api/UnitAtlas.Api.csproj
COPY src src
RUN dotnet publish src/UnitAtlas.Api/UnitAtlas.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "UnitAtlas.Api.dll"]
