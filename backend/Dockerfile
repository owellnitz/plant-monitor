FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY PlantMonitor.Backend/PlantMonitor.Backend.csproj PlantMonitor.Backend/
RUN dotnet restore PlantMonitor.Backend/PlantMonitor.Backend.csproj
COPY PlantMonitor.Backend/ PlantMonitor.Backend/
RUN dotnet publish PlantMonitor.Backend/PlantMonitor.Backend.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "PlantMonitor.Backend.dll"]
