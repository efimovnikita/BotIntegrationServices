FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

# Install FFmpeg and other dependencies
RUN apt-get update && \
    apt-get install -y ffmpeg \
    libnss3 \
    libnspr4 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libatspi2.0-0 \
    libxcomposite1 \
    libxdamage1 \
    libgtk-3-0 \
    libgstreamer1.0-0 \
    libatomic1 \
    libxslt1.1 \
    libwoff1 \
    libevent-2.1-7 \
    libgstreamer-plugins-base1.0-0 \
    libgstreamer-plugins-bad1.0-0 \
    libwebpdemux2 \
    libharfbuzz-icu0 \
    libenchant-2-2 \
    libsecret-1-0 \
    libhyphen0 \
    libmanette-0.2-0 \
    libpsl5 \
    libnghttp2-14 \
    libgles2 && \
    rm -rf /var/lib/apt/lists/*

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

RUN chmod +x /app/.playwright/node/*/node

ENTRYPOINT ["dotnet", "BotIntegration.Services.YouTube.dll"]
