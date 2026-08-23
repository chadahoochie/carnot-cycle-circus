# Stage 1: Runtime Base
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000
ENV ASPNETCORE_HTTP_PORTS=5000 \
    ASPNETCORE_ENVIRONMENT=Production \
    CARNOT_DATA_DIR=/app/data \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Stage 2: SDK & Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy Solution configuration files
COPY ["global.json", "./"]
COPY ["Directory.Build.props", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["NuGet.Config", "./"]
COPY ["CarnotCycleCircus.slnx", "./"]

# Copy Project files
COPY ["src/CarnotCycleCircus.Core/CarnotCycleCircus.Core.csproj", "src/CarnotCycleCircus.Core/"]
COPY ["src/CarnotCycleCircus.Web/CarnotCycleCircus.Web.csproj", "src/CarnotCycleCircus.Web/"]
COPY ["tests/CarnotCycleCircus.Tests/CarnotCycleCircus.Tests.csproj", "tests/CarnotCycleCircus.Tests/"]

# Restore packages
RUN dotnet restore "CarnotCycleCircus.slnx"

# Copy source tree
COPY src/ src/
COPY tests/ tests/
COPY docs/ docs/

# Build and run unit tests inside builder to guarantee image integrity
RUN dotnet test "tests/CarnotCycleCircus.Tests/CarnotCycleCircus.Tests.csproj" -c Release --no-restore

# Publish Web application
FROM build AS publish
RUN dotnet publish "src/CarnotCycleCircus.Web/CarnotCycleCircus.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 3: Final Production Image
FROM base AS final
WORKDIR /app

# Create persistent storage mount points with standard permissions
RUN mkdir -p /app/data /app/data/artifacts /app/data/skills /app/data/artifacts/adrs

# Copy compiled binaries from publish stage
COPY --from=publish /app/publish .

# Declare persistent volume mount point
VOLUME ["/app/data"]

# Health check against built-in endpoint
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD curl --fail http://localhost:5000/health || exit 1

ENTRYPOINT ["dotnet", "CarnotCycleCircus.Web.dll"]
