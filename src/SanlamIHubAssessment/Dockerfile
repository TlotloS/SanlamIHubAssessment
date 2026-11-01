FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# Build context is the repository root (see compose.yaml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/SanlamIHubAssessment/SanlamIHubAssessment.csproj", "src/SanlamIHubAssessment/"]
RUN dotnet restore "src/SanlamIHubAssessment/SanlamIHubAssessment.csproj"
COPY src/ src/
WORKDIR "/src/src/SanlamIHubAssessment"
RUN dotnet build "./SanlamIHubAssessment.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SanlamIHubAssessment.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SanlamIHubAssessment.dll"]
