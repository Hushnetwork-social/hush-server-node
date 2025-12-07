# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy all the necessary files and restore dependencies
# Copying .sln and .csproj files first allows Docker to cache the restore step
COPY HushNetwork.sln .
COPY ["Node/HushServerNode/HushServerNode.csproj", "Node/HushServerNode/"]
COPY ["Shared/Olimpo.BootstrapperManager/BootstrapperManager.csproj", "Shared/Olimpo.BootstrapperManager/"]
COPY ["Shared/Olimpo.EventAggregatorManager/EventAggregatorManager.csproj", "Shared/Olimpo.EventAggregatorManager/"]
COPY ["Node/Infrastructure/HushNode.Credentials/HushNode.Credentials.csproj", "Node/Infrastructure/HushNode.Credentials/"]
# ... Add COPY commands for all referenced projects in HushServerNode.csproj ...
# This part can be tedious. For now, let's copy everything to ensure it builds.
# A more optimized Dockerfile would copy only the necessary projects.
COPY . .

WORKDIR "/src/Node/HushServerNode"
RUN dotnet restore

WORKDIR "/src"
RUN dotnet publish "Node/HushServerNode/HushServerNode.csproj" -c Release -o /app/publish

# Use the ASP.NET runtime image for the final, smaller container
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

# Expose the port the gRPC server will listen on
EXPOSE 4665

ENTRYPOINT ["dotnet", "HushServerNode.dll"]
