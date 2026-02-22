# Dockerfile for Agitprop.Scraper.Consumer (background service)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# copy the full repo so Directory.Packages.props is available and restore
COPY . .
WORKDIR /src/Agitprop.Scraper.Consumer
RUN dotnet restore "Agitprop.Scraper.Consumer.csproj"

# publish (restore already done)
RUN dotnet publish "Agitprop.Scraper.Consumer.csproj" -c Release -o /app/publish --no-restore

# runtime image – use ASP.NET runtime (contains Microsoft.AspNetCore.App)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Agitprop.Scraper.Consumer.dll"]
