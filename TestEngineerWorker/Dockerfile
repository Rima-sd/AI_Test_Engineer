
# ============================================================
# Purpose:
#   Build a production-ready Linux container image for the
#   TestEngineerWorker validation application.
#
# Usage:
#   Build from the solution root:
#   docker build -f TestEngineerWorker/Dockerfile -t testengineer-worker:local .
#
# Configuration:
#   Runtime configuration is supplied through environment
#   variables. Do not place secrets in the Docker image.
#
# Responsibilities:
#   1. Restore Worker and Contracts dependencies.
#   2. Publish the .NET 10 Worker application.
#   3. Run the application using the lightweight .NET runtime.
#   4. Execute as a non-root user.
# ============================================================

# Stage 1: Build and publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files first for better Docker layer caching.
COPY ["TestEngineer.Contracts/TestEngineer.Contracts.csproj", "TestEngineer.Contracts/"]
COPY ["TestEngineerWorker/TestEngineerWorker.csproj", "TestEngineerWorker/"]

# Restore NuGet dependencies.
RUN dotnet restore "TestEngineerWorker/TestEngineerWorker.csproj"

# Copy the source code.
COPY . .

# Publish the Worker and its project dependencies.
WORKDIR /src/TestEngineerWorker

RUN dotnet publish "TestEngineerWorker.csproj" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS final

WORKDIR /app

# Copy published application from build stage.
COPY --from=build /app/publish .

# Run as a non-root user.
USER $APP_UID

# Start the Worker.
ENTRYPOINT ["dotnet", "TestEngineerWorker.dll"]
