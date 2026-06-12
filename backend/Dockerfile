# Build context is the repo root: the image bundles the Angular frontend
# (served by Kestrel from wwwroot) with the backend.
FROM node:26-alpine AS frontend
WORKDIR /src
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/PlantMonitor.Backend/PlantMonitor.Backend.csproj PlantMonitor.Backend/
RUN dotnet restore PlantMonitor.Backend/PlantMonitor.Backend.csproj
COPY backend/PlantMonitor.Backend/ PlantMonitor.Backend/
RUN dotnet publish PlantMonitor.Backend/PlantMonitor.Backend.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
COPY --from=build /app .
COPY --from=frontend /src/dist/frontend/browser ./wwwroot
ENTRYPOINT ["dotnet", "PlantMonitor.Backend.dll"]
