FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY MonsutaPotaru/MonsutaPotaru.csproj MonsutaPotaru/
RUN dotnet restore MonsutaPotaru/MonsutaPotaru.csproj
COPY MonsutaPotaru/ MonsutaPotaru/
RUN dotnet publish MonsutaPotaru/MonsutaPotaru.csproj -c Release -o /app/publish --no-restore

# Combats : simulateur de Pokémon Showdown (showdown/combat.js), lancé par le site avec Node.js pour chaque combat.
FROM node:22-bookworm-slim AS showdown
WORKDIR /showdown
COPY showdown/package.json showdown/package-lock.json ./
RUN npm ci --omit=dev
COPY showdown/combat.js ./

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=showdown /usr/local/bin/node /usr/local/bin/node
COPY --from=showdown /showdown /app/showdown
# Base SQLite + copies des sauvegardes : à monter en volume.
RUN mkdir -p /app/Data && chown -R app:app /app/Data
VOLUME /app/Data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MonsutaPotaru.dll"]
