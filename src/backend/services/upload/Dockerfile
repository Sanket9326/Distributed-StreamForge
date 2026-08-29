FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY StreamForge.Upload.Api.csproj ./
RUN dotnet restore StreamForge.Upload.Api.csproj --source https://api.nuget.org/v3/index.json
COPY . ./
RUN dotnet publish StreamForge.Upload.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "StreamForge.Upload.Api.dll"]
