FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ../BabyStepsMultiplayerServer/* .
RUN dotnet restore BabyStepsMultiplayerServer.csproj

RUN dotnet publish BabyStepsMultiplayerServer.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY docker/docker-entrypoint.sh /app/entrypoint.sh

EXPOSE 7777

LABEL org.opencontainers.image.description A headless C# server for the Baby Steps Multiplayer Mod. 

ENV DOTNET_ENVIRONMENT=Production

RUN chmod +x /app/entrypoint.sh
ENTRYPOINT ["/app/entrypoint.sh"]

CMD ["dotnet", "BabyStepsMultiplayerServer.dll"]
