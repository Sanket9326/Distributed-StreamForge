FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine3.23 AS build
WORKDIR /src
COPY StreamForge.Transcoding.Worker.csproj ./
RUN dotnet restore StreamForge.Transcoding.Worker.csproj --source https://api.nuget.org/v3/index.json
COPY . ./
RUN dotnet publish StreamForge.Transcoding.Worker.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine3.23 AS final
ARG FFMPEG_PACKAGE_VERSION=8.0.1-r1
RUN apk add --no-cache "ffmpeg=${FFMPEG_PACKAGE_VERSION}" \
    && mkdir -p /tmp/streamforge-transcoding \
    && chown app:app /tmp/streamforge-transcoding
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "StreamForge.Transcoding.Worker.dll"]
