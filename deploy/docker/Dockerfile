FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/hosts/SkillHorizon.Api/SkillHorizon.Api.csproj -c Release -o /app/api /p:UseAppHost=false \
 && dotnet publish src/hosts/SkillHorizon.Worker/SkillHorizon.Worker.csproj -c Release -o /app/worker /p:UseAppHost=false \
 && dotnet publish src/hosts/SkillHorizon.Control/SkillHorizon.Control.csproj -c Release -o /app/control /p:UseAppHost=false \
 && dotnet publish src/hosts/SkillHorizon.Web/SkillHorizon.Web.csproj -c Release -o /app/web /p:UseAppHost=false

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
