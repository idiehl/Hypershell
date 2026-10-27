# Build from the repository root:
#   docker build -f src/Hypershell.ProfileServer/Dockerfile -t hypershell-profile-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Hypershell.ProfileProtocol/Hypershell.ProfileProtocol.csproj src/Hypershell.ProfileProtocol/
COPY src/Hypershell.ProfileServer/Hypershell.ProfileServer.csproj src/Hypershell.ProfileServer/
COPY src/Hypershell/Profiles/ProfileSyncContracts.cs src/Hypershell/Profiles/
RUN dotnet restore src/Hypershell.ProfileServer/Hypershell.ProfileServer.csproj
COPY src/Hypershell.ProfileProtocol/ src/Hypershell.ProfileProtocol/
COPY src/Hypershell.ProfileServer/ src/Hypershell.ProfileServer/
RUN dotnet publish src/Hypershell.ProfileServer/Hypershell.ProfileServer.csproj \
    -c Release \
    --no-restore \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN mkdir -p /var/lib/hypershell-profile-server \
    && chown -R app:app /var/lib/hypershell-profile-server
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    HYPERSHELL_PROFILE_SERVER_DATA=/var/lib/hypershell-profile-server
EXPOSE 8080
VOLUME ["/var/lib/hypershell-profile-server"]
COPY --from=build --chown=app:app /app/publish/ .
ENTRYPOINT ["dotnet", "Hypershell.ProfileServer.dll"]
