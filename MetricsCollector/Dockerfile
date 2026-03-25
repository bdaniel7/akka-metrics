FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Shared/Shared.csproj", "Shared/"]
COPY ["MetricsCollector/MetricsCollector.csproj", "MetricsCollector/"]
RUN dotnet restore "MetricsCollector/MetricsCollector.csproj"
COPY . .
WORKDIR "/src/MetricsCollector"
RUN dotnet build "MetricsCollector.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MetricsCollector.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MetricsCollector.dll"]
