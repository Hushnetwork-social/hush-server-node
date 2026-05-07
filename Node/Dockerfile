# Use the .NET SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy all the necessary files and restore dependencies
# Copying .sln and .csproj files first allows Docker to cache the restore step
COPY ["Node/HushServerNode.sln", "Node/"]
COPY ["Node/HushServerNode/HushServerNode.csproj", "Node/HushServerNode/"]
COPY ["Shared/Olimpo.BootstrapperManager/BootstrapperManager.csproj", "Shared/Olimpo.BootstrapperManager/"]
COPY ["Shared/Olimpo.EventAggregatorManager/EventAggregatorManager.csproj", "Shared/Olimpo.EventAggregatorManager/"]
COPY ["Node/Infrastructure/HushNode.Credentials/HushNode.Credentials.csproj", "Node/Infrastructure/HushNode.Credentials/"]
# ... Add COPY commands for all referenced projects in HushServerNode.csproj ...
# This part can be tedious. For now, let's copy everything to ensure it builds.
# A more optimized Dockerfile would copy only the necessary projects.
COPY . .

WORKDIR "/src"
RUN dotnet restore "Node/HushServerNode.sln"

RUN dotnet publish "Node/HushServerNode/HushServerNode.csproj" -c Release -o /app/publish

FROM rust:1-bookworm AS sp07-worker-build
WORKDIR /src
COPY Tools/HushSp07RustWorker Tools/HushSp07RustWorker
WORKDIR /src/Tools/HushSp07RustWorker
RUN cargo build --release --locked

FROM node:24-bookworm-slim AS zk-runtime
WORKDIR /opt/hush-snarkjs
RUN npm init -y
RUN npm install --omit=dev snarkjs@0.7.5

# Use the ASP.NET runtime image for the final, smaller container
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=sp07-worker-build /src/Tools/HushSp07RustWorker/target/release/hush-sp07-rust-worker /app/tools/hush-sp07/hush-sp07-rust-worker
COPY --from=zk-runtime /usr/local/ /usr/local/
COPY --from=zk-runtime /opt/hush-snarkjs /opt/hush-snarkjs

ENV SNARKJS_MODULE_ROOT=/opt/hush-snarkjs
ENV HUSH_SP07_RUST_WORKER_PATH=/app/tools/hush-sp07/hush-sp07-rust-worker

# Expose gRPC ports:
# - 4665: Native gRPC (HTTP/2) for Desktop, Android, iOS
# - 4666: gRPC-Web (HTTP/1.1) for Browser/WebAssembly
EXPOSE 4665
EXPOSE 4666

ENTRYPOINT ["dotnet", "HushServerNode.dll"]
