# Multi-stage build for the Blazor Server web app (docs §6.3).
# Build context must be the solution root (News-Aggregator/src) so that
# Directory.*.props and the referenced projects are available:
#   docker build -f NewsAggregator.Web/Dockerfile -t newsaggregator-web .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore (leverage layer caching: copy csproj/props first).
COPY ["Directory.Build.props", "Directory.Packages.props", "global.json", "./"]
COPY ["NewsAggregator.Core/NewsAggregator.Core.csproj", "NewsAggregator.Core/"]
COPY ["NewsAggregator.Infrastructure/NewsAggregator.Infrastructure.csproj", "NewsAggregator.Infrastructure/"]
COPY ["NewsAggregator.Web/NewsAggregator.Web.csproj", "NewsAggregator.Web/"]
RUN dotnet restore "NewsAggregator.Web/NewsAggregator.Web.csproj"

# Copy the rest and publish.
COPY . .
RUN dotnet publish "NewsAggregator.Web/NewsAggregator.Web.csproj" \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "NewsAggregator.Web.dll"]
