# ==================== Build Stage ====================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY MinGo.MyBillBook.slnx ./
COPY src/MinGo.MyBillBook.Core/MinGo.MyBillBook.Core.csproj src/MinGo.MyBillBook.Core/
COPY src/MinGo.MyBillBook.Data/MinGo.MyBillBook.Data.csproj src/MinGo.MyBillBook.Data/
COPY src/MinGo.MyBillBook.Client/MinGo.MyBillBook.Client.csproj src/MinGo.MyBillBook.Client/
COPY src/MinGo.MyBillBook/MinGo.MyBillBook.csproj src/MinGo.MyBillBook/
COPY tests/MinGo.MyBillBook.Tests/MinGo.MyBillBook.Tests.csproj tests/MinGo.MyBillBook.Tests/

# Restore dependencies (cached layer + NuGet cache mount)
# Only re-runs when csproj files change
RUN --mount=type=cache,id=nuget,target=/root/.nuget/packages \
    dotnet restore

# Copy all source code
COPY . .

# Build
RUN dotnet publish src/MinGo.MyBillBook/MinGo.MyBillBook.csproj \
    -c Release \
    -o /app/publish

# ==================== Runtime Stage ====================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Create non-root user
RUN useradd -m -d /app appuser

WORKDIR /app

# Create data directory for SQLite + DuckDB
RUN mkdir -p /app/data && chown appuser:appuser /app/data

COPY --from=build /app/publish .

# Switch to non-root user
USER appuser

# Environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ConnectionStrings__DefaultConnection="Data Source=/app/data/mybillbook.db"

# Data volume for persistent storage
VOLUME ["/app/data"]

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/ || exit 1

ENTRYPOINT ["dotnet", "MinGo.MyBillBook.dll"]
