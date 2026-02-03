FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG API_BASE_URL=http://localhost:8080/
ARG VERSION_MAJOR=0
ARG VERSION_MINOR=0
ARG VERSION_PATCH=0
ARG VERSION_PRERELEASE=
ARG VERSION_BUILD_METADATA=

WORKDIR /src

# Install Node.js and npm for Tailwind CSS
RUN apt-get update && \
    apt-get install -y curl && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y nodejs && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

# Install Tailwind CSS CLI globally
RUN npm install -g tailwindcss

# Copy from solution root (build context should be the solution directory)
COPY . .

# Set the API base URL used by the browser app (baked into the published static files)
RUN echo "Setting Api:BaseUrl to ${API_BASE_URL}" \
    && sed -i "s#\"BaseUrl\"\\s*:\\s*\"[^\"]*\"#\"BaseUrl\": \"${API_BASE_URL}\"#g" src/WebFrontend/wwwroot/appsettings.json

# Set version information in appsettings.json
RUN echo "Setting Version fields" \
    && sed -i "s#\"Major\"\\s*:\\s*[0-9]*#\"Major\": ${VERSION_MAJOR}#g" src/WebFrontend/wwwroot/appsettings.json \
    && sed -i "s#\"Minor\"\\s*:\\s*[0-9]*#\"Minor\": ${VERSION_MINOR}#g" src/WebFrontend/wwwroot/appsettings.json \
    && sed -i "s#\"Patch\"\\s*:\\s*[0-9]*#\"Patch\": ${VERSION_PATCH}#g" src/WebFrontend/wwwroot/appsettings.json \
    && if [ -n "${VERSION_PRERELEASE}" ]; then \
        sed -i "s#\"PreRelease\"\\s*:\\s*[^,}]*#\"PreRelease\": \"${VERSION_PRERELEASE}\"#g" src/WebFrontend/wwwroot/appsettings.json; \
    fi \
    && if [ -n "${VERSION_BUILD_METADATA}" ]; then \
        sed -i "s#\"BuildMetadata\"\\s*:\\s*[^,}]*#\"BuildMetadata\": \"${VERSION_BUILD_METADATA}\"#g" src/WebFrontend/wwwroot/appsettings.json; \
    fi

RUN dotnet restore "./src/WebFrontend/WebFrontend.csproj"
RUN dotnet publish "./src/WebFrontend/WebFrontend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Serve the published wwwroot via nginx
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

# Copy published files
COPY --from=build /app/publish/wwwroot ./

# Remove development configuration file if present
RUN rm -f appsettings.Development.json || true

# Copy nginx config
COPY ./src/WebFrontend/nginx.conf /etc/nginx/conf.d/default.conf

# Copy and set up entrypoint script for runtime config replacement
COPY ./src/WebFrontend/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 8080

# Use custom entrypoint that replaces config values at runtime
ENTRYPOINT ["/docker-entrypoint.sh"]
