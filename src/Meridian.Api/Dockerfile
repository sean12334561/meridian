# Build from the repo root: docker build -f src/Meridian.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Meridian.Api/Meridian.Api.csproj src/Meridian.Api/
RUN dotnet restore src/Meridian.Api/Meridian.Api.csproj
COPY src/ src/
# contracts/openapi.json is checked in, so the image build doesn't regenerate it
RUN dotnet publish src/Meridian.Api/Meridian.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false /p:OpenApiGenerateDocumentsOnBuild=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER $APP_UID
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Meridian.Api.dll"]
