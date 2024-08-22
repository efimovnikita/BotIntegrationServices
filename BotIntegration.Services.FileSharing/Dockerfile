FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BotIntegration.Services.FileSharing/BotIntegration.Services.FileSharing.csproj", "BotIntegration.Services.FileSharing/"]
RUN dotnet restore "BotIntegration.Services.FileSharing/BotIntegration.Services.FileSharing.csproj"
COPY . .
WORKDIR "/src/BotIntegration.Services.FileSharing"
RUN dotnet build "BotIntegration.Services.FileSharing.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BotIntegration.Services.FileSharing.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

RUN mkdir -p /app/wwwroot/uploads && chown -R $APP_UID:$APP_UID /app/wwwroot/uploads

ENTRYPOINT ["dotnet", "BotIntegration.Services.FileSharing.dll"]
