# Use the official .NET SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ExchangeCalendarSync.csproj ./
RUN dotnet restore ExchangeCalendarSync.csproj

# Copy the rest of the application (excluding test project)
COPY . ./

# Build the application (explicitly target the project, not the solution)
RUN dotnet publish ExchangeCalendarSync.csproj -c Release -o /app/publish --no-restore

# Use the ASP.NET runtime image for the final container
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install any required system dependencies
RUN apt-get update && apt-get install -y \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Copy the published application
COPY --from=build /app/publish .

# Set environment variables
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5000

# Expose the web interface port
EXPOSE 5000

# Run the application
ENTRYPOINT ["dotnet", "ExchangeCalendarSync.dll"]
