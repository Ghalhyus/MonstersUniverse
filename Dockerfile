FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY PokeBank/PokeBank.csproj PokeBank/
RUN dotnet restore PokeBank/PokeBank.csproj
COPY PokeBank/ PokeBank/
RUN dotnet publish PokeBank/PokeBank.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
# Base SQLite + copies des sauvegardes : à monter en volume.
RUN mkdir -p /app/Data && chown -R app:app /app/Data
VOLUME /app/Data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PokeBank.dll"]
