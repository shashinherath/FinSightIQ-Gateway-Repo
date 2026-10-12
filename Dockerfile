# ==============================================================================
# FinSightIQ API Gateway — Multi-stage Containerfile (.NET 8)
# ==============================================================================

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project definitions first for optimal layer caching
COPY ["global.json", "./"]
COPY ["nuget.config", "./"]
COPY ["packages/", "./packages/"]
COPY ["src/FinSightIQ.Gateway/FinSightIQ.Gateway.csproj", "src/FinSightIQ.Gateway/"]

# Restore packages with optional BuildKit secret for GitHub Packages access
RUN --mount=type=secret,id=nuget_token,required=false \
    if [ -f /run/secrets/nuget_token ]; then \
        export NUGET_TOKEN=$(cat /run/secrets/nuget_token); \
    fi && \
    dotnet restore "src/FinSightIQ.Gateway/FinSightIQ.Gateway.csproj"

# Copy source code and build
COPY ["src/FinSightIQ.Gateway/", "src/FinSightIQ.Gateway/"]
WORKDIR "/src/src/FinSightIQ.Gateway"

RUN dotnet build "FinSightIQ.Gateway.csproj" -c Release --no-restore
RUN dotnet publish "FinSightIQ.Gateway.csproj" -c Release --no-build -o /app/publish /p:UseAppHost=false

# Stage 2: Production Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Run as built-in non-root app user
USER $APP_UID

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000 \
    ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5000

HEALTHCHECK --interval=10s --timeout=3s --retries=3 --start-period=5s \
    CMD curl -f http://localhost:5000/health/live || exit 1

ENTRYPOINT ["dotnet", "FinSightIQ.Gateway.dll"]
