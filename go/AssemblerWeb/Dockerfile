# syntax=docker/dockerfile:1

# Build stage
FROM golang:alpine AS build
RUN apk add --no-cache curl
RUN curl -L https://github.com/DarthSim/overmind/releases/download/v2.4.0/overmind-v2.4.0-linux-amd64.gz | gunzip -c > /usr/local/bin/overmind && chmod +x /usr/local/bin/overmind

WORKDIR /src
COPY ./Assembler ./Assembler
COPY ./AssemblerWeb ./AssemblerWeb
RUN cd AssemblerWeb && go build -o assemblerweb .

# Runtime stage
FROM alpine:latest AS final
RUN apk upgrade --no-cache --available && \
    apk add tmux && \
    rm -rf /var/cache/apk/*
COPY --from=build /usr/local/bin/overmind /usr/local/bin/overmind

WORKDIR /app
COPY --from=build /src/AssemblerWeb/assemblerweb .
COPY --from=build /src/AssemblerWeb/wwwroot ./wwwroot

EXPOSE 8080
RUN echo 'go-assembler: /app/assemblerweb'  > Procfile
CMD ["/bin/sh", "-c", "rm -f /app/.overmind.sock; /usr/local/bin/overmind start -f ./Procfile"]
