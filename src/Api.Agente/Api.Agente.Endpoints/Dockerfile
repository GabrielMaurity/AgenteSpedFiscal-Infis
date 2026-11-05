# Contexto de build: raiz do repositório (ver docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props .editorconfig ./
COPY data/ data/
COPY src/ src/
RUN dotnet publish src/Api.Agente/Api.Agente.Endpoints/Api.Agente.Endpoints.csproj -c Release -o /app/publish
RUN mkdir -p /uploads

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
# Volume temporário de uploads gravável pelo usuário não root da imagem (APP_UID = 1654).
COPY --from=build --chown=1654:1654 /uploads /var/tmp/uploads
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Api.Agente.Endpoints.dll"]
