FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BotIntegration.Services.ArtifactsRepo.FrontEnd/BotIntegration.Services.ArtifactsRepo.FrontEnd.csproj", "BotIntegration.Services.ArtifactsRepo.FrontEnd/"]
COPY ["BotIntegration.Shared/BotIntegration.Shared.csproj", "BotIntegration.Shared/"]
RUN dotnet restore "BotIntegration.Services.ArtifactsRepo.FrontEnd/BotIntegration.Services.ArtifactsRepo.FrontEnd.csproj"
COPY . .
WORKDIR "/src/BotIntegration.Services.ArtifactsRepo.FrontEnd"
RUN dotnet build "BotIntegration.Services.ArtifactsRepo.FrontEnd.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BotIntegration.Services.ArtifactsRepo.FrontEnd.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BotIntegration.Services.ArtifactsRepo.FrontEnd.dll"]
