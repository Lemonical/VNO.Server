FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/VNO.Server/VNO.Server.csproj src/VNO.Server/
COPY external/VNO.Core/src/VNO.Core/VNO.Core.csproj external/VNO.Core/src/VNO.Core/
RUN dotnet restore src/VNO.Server/VNO.Server.csproj

COPY src/VNO.Server/ src/VNO.Server/
COPY external/VNO.Core/ external/VNO.Core/
RUN dotnet publish src/VNO.Server/VNO.Server.csproj -c Release -o /app --no-restore -p:UseAppHost=false

FROM node:22-alpine AS console-build
WORKDIR /console
COPY clients/server-console/package.json clients/server-console/package-lock.json ./
RUN npm ci
COPY clients/server-console/tsconfig.json ./
COPY clients/server-console/source/ source/
RUN npm run build && npm prune --omit=dev

FROM node:22-alpine AS console
ARG RUNTIME_UID=1654
WORKDIR /console
COPY --from=console-build --chown=${RUNTIME_UID}:${RUNTIME_UID} /console/package.json /console/package-lock.json ./
COPY --from=console-build --chown=${RUNTIME_UID}:${RUNTIME_UID} /console/node_modules/ node_modules/
COPY --from=console-build --chown=${RUNTIME_UID}:${RUNTIME_UID} /console/dist/ dist/
USER ${RUNTIME_UID}
ENTRYPOINT ["node", "dist/cli.js"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
RUN mkdir -p /app/data && chown -R $APP_UID:$APP_UID /app

ENV VNO_DATA_DIRECTORY=/app/data \
    VNO_SERVER_PORT=6541 \
    VNO_SERVER_TRANSPORT=WebSocket

USER $APP_UID
VOLUME ["/app/data"]
EXPOSE 6541 6542
ENTRYPOINT ["dotnet", "VNO.Server.dll", "--headless"]
