# Build & Publish Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS publish
WORKDIR /src

COPY ["Gram.Blazor/Gram.Blazor.csproj", "Gram.Blazor/"]
RUN dotnet restore "Gram.Blazor/Gram.Blazor.csproj"

COPY . .
WORKDIR "/src/Gram.Blazor"
RUN dotnet publish "Gram.Blazor.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates curl && rm -rf /var/lib/apt/lists/*
EXPOSE 7337
ENV PORT=7337
ENV ASPNETCORE_URLS=http://+:7337

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Gram.Blazor.dll"]
