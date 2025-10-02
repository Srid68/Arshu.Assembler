#run from rust directory
#podman build -f AssemblerWeb/Dockerfile -t rustassembler:latest .

# Build stage
FROM rust:alpine AS build
RUN apk add --no-cache musl-dev openssl-dev pkgconfig curl
RUN curl -L https://github.com/DarthSim/overmind/releases/download/v2.4.0/overmind-v2.4.0-linux-amd64.gz | gunzip -c > /usr/local/bin/overmind && chmod +x /usr/local/bin/overmind

WORKDIR /src
COPY ./Assembler ./Assembler
COPY ./AssemblerWeb ./AssemblerWeb
RUN cargo build --release --manifest-path ./AssemblerWeb/Cargo.toml

# Runtime stage
FROM alpine:latest AS final
RUN apk upgrade --no-cache --available && \
    apk add tmux && \
    rm -rf /var/cache/apk/*
RUN ln -sf /usr/bin/tmux /bin/tmux    
RUN tmux -V
COPY --from=build /usr/local/bin/overmind /usr/local/bin/overmind

WORKDIR /app
COPY --from=build /src/AssemblerWeb/target/release/assembler_web .
COPY --from=build /src/AssemblerWeb/wwwroot ./wwwroot

EXPOSE 8080
RUN echo 'rust-assembler: /app/assembler_web'  > Procfile
CMD ["/bin/sh", "-c", "rm -f /app/.overmind.sock; /usr/local/bin/overmind start -f ./Procfile"]

