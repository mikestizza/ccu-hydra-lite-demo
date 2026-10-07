# Hydra web tier - standard .NET multi-stage build.
# Stage 1 compiles with the SDK image; stage 2 is the small ASP.NET runtime image
# that actually ships. No IIS, no Windows, runs as a non-root user.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY HydraWeb.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG APP_VERSION=dev
ARG GIT_SHA=local
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    APP_VERSION=${APP_VERSION} \
    GIT_SHA=${GIT_SHA} \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER $APP_UID
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "HydraWeb.dll"]
