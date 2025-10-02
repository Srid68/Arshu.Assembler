#run from csharp directory
#podman build -f AssemblerWebJs/Dockerfile -t jsassembler:latest .
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
RUN apk add --no-cache clang gcc g++ musl-dev
RUN curl -L https://github.com/DarthSim/overmind/releases/download/v2.4.0/overmind-v2.4.0-linux-amd64.gz | gunzip -c > /usr/local/bin/overmind && chmod +x /usr/local/bin/overmind

WORKDIR /src
COPY ./Assembler ./Assembler
COPY ./AssemblerWebJs ./AssemblerWebJs
RUN dotnet restore ./AssemblerWebJs/AssemblerWebJs.csproj
RUN dotnet publish ./AssemblerWebJs/AssemblerWebJs.csproj -c Release -o /app/publish /p:PublishAot=true

# NativeAOT runtime stage
FROM alpine:latest AS final
RUN apk upgrade --no-cache --available && \
    apk add tmux && \
    rm -rf /var/cache/apk/*
COPY --from=build usr/local/bin/overmind usr/local/bin/overmind

WORKDIR /app
COPY --from=build /app/publish .
COPY ./AssemblerWebJs/wwwroot ./wwwroot

EXPOSE 8080
RUN echo 'js-assembler: ./AssemblerWebJs --urls http://0.0.0.0:8080' > Procfile
CMD ["/bin/sh", "-c", "rm -f /app/.overmind.sock;//usr/local/bin/overmind start -f ./Procfile"]
