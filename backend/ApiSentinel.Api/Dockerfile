FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/ApiSentinel.Api/ApiSentinel.Api.csproj backend/ApiSentinel.Api/
RUN dotnet restore backend/ApiSentinel.Api/ApiSentinel.Api.csproj
COPY backend/ApiSentinel.Api/ backend/ApiSentinel.Api/
RUN dotnet publish backend/ApiSentinel.Api/ApiSentinel.Api.csproj --configuration Release --no-restore --output /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 DataProtection__KeysPath=/keys
EXPOSE 8080
RUN mkdir -p /keys && chown app:app /keys
COPY --from=build /out .
ENTRYPOINT ["dotnet", "ApiSentinel.Api.dll"]