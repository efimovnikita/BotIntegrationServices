FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# Switch to root user to install ffmpeg
USER root
# Install ffmpeg
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    # Clean up to reduce image size
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/*
# Return to the application user
USER $APP_UID
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BotIntegration.Services.Audio/BotIntegration.Services.Audio.csproj", "BotIntegration.Services.Audio/"]
RUN dotnet restore "BotIntegration.Services.Audio/BotIntegration.Services.Audio.csproj"
COPY . .
WORKDIR "/src/BotIntegration.Services.Audio"
RUN dotnet build "BotIntegration.Services.Audio.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BotIntegration.Services.Audio.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BotIntegration.Services.Audio.dll"]
