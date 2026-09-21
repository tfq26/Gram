# Build Stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
ARG TARGETARCH
WORKDIR /source

# Copy project files and restore dependencies
COPY *.csproj .
COPY Gram.Server/*.csproj Gram.Server/
RUN dotnet restore Gram.Server/Gram.Server.csproj -a $TARGETARCH

# Copy full application sources and publish
COPY . .
RUN dotnet publish Gram.Server/Gram.Server.csproj -c Release -a $TARGETARCH -o /app/publish --no-restore

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS final
WORKDIR /app

# Configure default runtime environment
ENV ASPNETCORE_URLS=http://+:7337 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    GRAM_DATA_DIR=/app/data

# Create data and logs directory
RUN mkdir -p /app/data /app/logs && \
    addgroup -g 1000 gram && \
    adduser -u 1000 -G gram -s /bin/sh -D gram && \
    chown -R gram:gram /app

# Copy compiled binaries from build stage
COPY --from=build --chown=gram:gram /app/publish .

# Run as non-root user for security
USER gram

# Expose Web Port
EXPOSE 7337

# Healthcheck definition
HEALTHCHECK --interval=30s --timeout=5s --start-period=5s --retries=3 \
  CMD wget -qO- http://localhost:7337/health || exit 1

ENTRYPOINT ["dotnet", "Gram.Server.dll"]
