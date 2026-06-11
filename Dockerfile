# Etapa de ejecución
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8081
EXPOSE 8081

# Etapa de construcción
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copiar solo el csproj del servidor para restaurar dependencias
COPY ["Servidor/Servidor.csproj", "Servidor/"]
RUN dotnet restore "./Servidor/Servidor.csproj"

# Copiar el resto de los archivos y compilar
COPY Servidor/ Servidor/
WORKDIR "/src/Servidor"
RUN dotnet build "./Servidor.csproj" -c Release -o /app/build

# Etapa de publicación
FROM build AS publish
RUN dotnet publish "./Servidor.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Etapa final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Servidor.dll"]