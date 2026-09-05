FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY StreamForge.Identity.Api.csproj ./
RUN dotnet restore StreamForge.Identity.Api.csproj --source https://api.nuget.org/v3/index.json
COPY . ./
RUN dotnet publish StreamForge.Identity.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "StreamForge.Identity.Api.dll"]
