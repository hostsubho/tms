FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Tms.Api/Tms.Api.csproj src/Tms.Api/
RUN dotnet restore src/Tms.Api/Tms.Api.csproj
COPY . .
RUN dotnet publish src/Tms.Api/Tms.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Tms.Api.dll"]
