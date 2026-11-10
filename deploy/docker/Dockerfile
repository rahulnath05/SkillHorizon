FROM node:22-bookworm-slim AS web
WORKDIR /web
COPY src/web/package.json src/web/package-lock.json ./
RUN npm ci
COPY src/web/ ./
ENV SKILLHORIZON_WEB_OUT=/out
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
COPY --from=web /out ./src/hosts/SkillHorizon.Web/wwwroot
RUN dotnet publish src/hosts/SkillHorizon.Api/SkillHorizon.Api.csproj -c Release -o /app/api /p:UseAppHost=false \
 && dotnet publish src/hosts/SkillHorizon.Worker/SkillHorizon.Worker.csproj -c Release -o /app/worker /p:UseAppHost=false \
 && dotnet publish src/hosts/SkillHorizon.Control/SkillHorizon.Control.csproj -c Release -o /app/control /p:UseAppHost=false \
 && dotnet publish src/hosts/SkillHorizon.Web/SkillHorizon.Web.csproj -c Release -o /app/web /p:UseAppHost=false /p:SkipWebClientBuild=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
COPY --from=build /app /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
