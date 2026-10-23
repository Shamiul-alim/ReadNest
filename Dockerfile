# Multi-stage build for the ReadNest web application.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ReadNest.Domain/ReadNest.Domain.csproj src/ReadNest.Domain/
COPY src/ReadNest.Application/ReadNest.Application.csproj src/ReadNest.Application/
COPY src/ReadNest.Infrastructure/ReadNest.Infrastructure.csproj src/ReadNest.Infrastructure/
COPY src/ReadNest.Web/ReadNest.Web.csproj src/ReadNest.Web/
RUN dotnet restore src/ReadNest.Web/ReadNest.Web.csproj
COPY src/ src/
RUN dotnet publish src/ReadNest.Web/ReadNest.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
# Uploaded profile photos and reading files. Mount a persistent volume here (see docker-compose.yml); files written
# inside the container's own filesystem are lost when the container is replaced.
RUN mkdir -p /data/storage && chown -R $APP_UID:$APP_UID /data
ENV Storage__RootPath=/data/storage
VOLUME ["/data"]
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ReadNest.Web.dll"]
