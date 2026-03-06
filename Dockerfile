# ─── Build Stage ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy csproj and restore
COPY src/TimeAuctionGame/TimeAuctionGame.csproj ./src/TimeAuctionGame/
RUN dotnet restore src/TimeAuctionGame/TimeAuctionGame.csproj

# Copy everything else and build
COPY src/ ./src/
RUN dotnet publish src/TimeAuctionGame/TimeAuctionGame.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ─── Runtime Stage ────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Render uses PORT environment variable
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "TimeAuctionGame.dll"]
