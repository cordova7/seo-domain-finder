# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SeoDomainFinder.Core/ src/SeoDomainFinder.Core/
COPY src/SeoDomainFinder.Infrastructure/ src/SeoDomainFinder.Infrastructure/
COPY src/SeoDomainFinder.Api/ src/SeoDomainFinder.Api/

RUN dotnet restore src/SeoDomainFinder.Api/SeoDomainFinder.Api.csproj
RUN dotnet publish src/SeoDomainFinder.Api/SeoDomainFinder.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SeoDomainFinder.Api.dll"]
