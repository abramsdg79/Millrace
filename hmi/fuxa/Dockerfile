# The millrace command line in a container: built from this repository's source
# with the .NET SDK, run on the .NET runtime, with the samples beside it.
# Build context: the repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/Millrace.Cli/Millrace.Cli.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/runtime:10.0.12-noble@sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5
WORKDIR /millrace
COPY --from=build /app /app
COPY samples/ samples/
EXPOSE 5020
# Not root: $APP_UID is the runtime image's unprivileged user.
USER $APP_UID
ENTRYPOINT ["/app/millrace"]
CMD ["serve", "samples/mine-conveyors/plant.json", "--port", "5020", "--bind", "0.0.0.0"]
