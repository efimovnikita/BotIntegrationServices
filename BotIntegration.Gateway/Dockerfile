FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

RUN addgroup --gid 1000 appgroup && \
    adduser --uid 1000 --ingroup appgroup --disabled-password --gecos "" appuser
    
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BotIntegration.Gateway/BotIntegration.Gateway.csproj", "BotIntegration.Gateway/"]
COPY ["BotIntegration.Shared/BotIntegration.Shared.csproj", "BotIntegration.Shared/"]
RUN dotnet restore "BotIntegration.Gateway/BotIntegration.Gateway.csproj"
COPY . .
WORKDIR "/src/BotIntegration.Gateway"
RUN dotnet build "BotIntegration.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BotIntegration.Gateway.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN chown appuser:appgroup /app/ocelot.json && chmod 666 /app/ocelot.json

USER appuser

ENTRYPOINT ["dotnet", "BotIntegration.Gateway.dll"]
