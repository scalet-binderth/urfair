# One container: the ASP.NET Core API also serves the built Angular app.

# 1. Build the Angular app (outputs into src/PointAllocation.Api/wwwroot)
FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build

# 2. Build and publish the API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json ./
COPY src/ src/
RUN dotnet publish src/PointAllocation.Api -c Release -o /app
COPY --from=web /src/src/PointAllocation.Api/wwwroot /app/wwwroot

# 3. Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/tmp/pointallocation.db"
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PointAllocation.Api.dll"]
