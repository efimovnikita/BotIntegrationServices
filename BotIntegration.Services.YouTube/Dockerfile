FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

RUN apt-get update && \
    apt-get install -y ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/*

WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BotIntegration.Services.YouTube/BotIntegration.Services.YouTube.csproj", "BotIntegration.Services.YouTube/"]
RUN dotnet restore "BotIntegration.Services.YouTube/BotIntegration.Services.YouTube.csproj"
COPY . .
WORKDIR "/src/BotIntegration.Services.YouTube"
RUN dotnet build "BotIntegration.Services.YouTube.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BotIntegration.Services.YouTube.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Copy yt-dlp_linux from Tools folder and make it executable
COPY --from=build /src/BotIntegration.Services.YouTube/Tools/yt-dlp_linux /app/Tools/
RUN chmod +x /app/Tools/yt-dlp_linux

ENTRYPOINT ["dotnet", "BotIntegration.Services.YouTube.dll"]
