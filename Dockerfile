# ── Stage 1: Build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先複製 csproj，讓 NuGet 還原層可被 Docker 快取
COPY src/TimeAuctionGame/TimeAuctionGame.csproj src/TimeAuctionGame/
RUN dotnet restore src/TimeAuctionGame/TimeAuctionGame.csproj

# 安裝 libman CLI，用來還原 wwwroot/lib 下的前端套件（Bootstrap、jQuery 等）
# 這些檔案由 .gitignore 排除，需在 build 階段從 CDN 下載
RUN dotnet tool install -g Microsoft.Web.LibraryManager.Cli
ENV PATH="$PATH:/root/.dotnet/tools"

# 複製其餘原始碼
COPY src/TimeAuctionGame/ src/TimeAuctionGame/

# 還原前端套件（從 cdnjs 下載到 wwwroot/lib）
RUN cd src/TimeAuctionGame && libman restore

# 發布（Release，自包含 = false，使用執行環境的 ASP.NET Runtime）
RUN dotnet publish src/TimeAuctionGame/TimeAuctionGame.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# 複製發布結果
COPY --from=build /app/publish .

# Render 會透過 PORT 環境變數傳入動態 port，ASP.NET Core 預設讀取 ASPNETCORE_URLS
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Render 在執行時才注入 PORT，所以改用 shell form 讓 $PORT 被展開
# 若 PORT 未設定（本機 docker run 測試），fallback 到 8080
CMD PORT=${PORT:-8080} && dotnet TimeAuctionGame.dll --urls "http://+:${PORT}"
