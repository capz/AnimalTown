FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY shared/ shared/
COPY server/AnimalTown.Server/ server/AnimalTown.Server/
RUN dotnet publish server/AnimalTown.Server -c Release -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet","AnimalTown.Server.dll"]
