FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["BotIntegration.Services.FileSharing.FrontEnd/BotIntegration.Services.FileSharing.FrontEnd.csproj", "BotIntegration.Services.FileSharing.FrontEnd/"]
RUN dotnet restore "BotIntegration.Services.FileSharing.FrontEnd/BotIntegration.Services.FileSharing.FrontEnd.csproj"
COPY . .
WORKDIR "/src/BotIntegration.Services.FileSharing.FrontEnd"
RUN dotnet build "BotIntegration.Services.FileSharing.FrontEnd.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "BotIntegration.Services.FileSharing.FrontEnd.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BotIntegration.Services.FileSharing.FrontEnd.dll"]
