# Imagen de la API (TG.Api). Se construye desde la raíz del repositorio:
#   docker build -f backend/Dockerfile -t tg-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY backend/ backend/
RUN dotnet restore backend/src/TG.Api/TG.Api.csproj
RUN dotnet publish backend/src/TG.Api/TG.Api.csproj -c Release -o /app --no-restore /p:OpenApiGenerateDocuments=false
# Bundle de migraciones de todos los módulos (se ejecuta en el despliegue, nunca al arrancar en producción).
RUN dotnet tool install --global dotnet-ef --version 10.* && export PATH="$PATH:/root/.dotnet/tools" \
 && for m in Catalogos Seguridad Inventario Compras Produccion Ventas Logistica Facturacion Sincronizacion; do \
      dotnet ef migrations bundle --project backend/src/Modules/TG.$m --startup-project backend/src/TG.Api \
        --context ${m}DbContext -o /app/migraciones/efbundle-$m --self-contained -r linux-x64 || exit 1; \
    done

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    TZ=UTC
RUN apt-get update && apt-get install -y --no-install-recommends tzdata libgdiplus fonts-dejavu-core && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD wget -qO- http://localhost:8080/salud || exit 1
ENTRYPOINT ["dotnet", "TG.Api.dll"]
