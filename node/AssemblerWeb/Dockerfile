# syntax=docker/dockerfile:1

# Build stage
FROM node:20-alpine AS build
RUN apk add --no-cache curl
RUN curl -L https://github.com/DarthSim/overmind/releases/download/v2.4.0/overmind-v2.4.0-linux-amd64.gz | gunzip -c > /usr/local/bin/overmind && chmod +x /usr/local/bin/overmind

WORKDIR /src
COPY ./Assembler ./Assembler
COPY ./AssemblerWeb ./AssemblerWeb
RUN cd AssemblerWeb && npm install && npm run build

# Runtime stage
FROM node:20-alpine AS final
RUN apk upgrade --no-cache --available && \
    apk add tmux && \
    rm -rf /var/cache/apk/*
COPY --from=build /usr/local/bin/overmind /usr/local/bin/overmind

WORKDIR /app
COPY --from=build /src/AssemblerWeb/index.js .
COPY --from=build /src/Assembler/src ./Assembler/src
COPY --from=build /src/AssemblerWeb/node_modules ./node_modules
COPY --from=build /src/AssemblerWeb/wwwroot ./wwwroot

EXPOSE 8080
ENV PORT=8080
RUN echo 'node-assembler: node /app/index.js'  > Procfile
CMD ["/bin/sh", "-c", "rm -f /app/.overmind.sock; /usr/local/bin/overmind start -f ./Procfile"]
