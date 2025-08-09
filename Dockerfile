# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Expose default Kestrel port (Render will still map it automatically)
EXPOSE 8080
# Let ASP.NET listen on 0.0.0.0:8080 (Render sets PORT env var at runtime too)
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENTRYPOINT ["dotnet", "GuitarTabApi.dll"]
