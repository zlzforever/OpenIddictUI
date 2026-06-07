# stage 1: 编译 Vue 前端
FROM node:22-alpine AS frontend
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
# vite.config.ts outDir='../src/OpenIddictUI/wwwroot' → /app/src/OpenIddictUI/wwwroot
RUN npm run build

# stage 2: 编译 .NET 后端
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /app
COPY *.slnx ./
COPY src/OpenIddictUI/OpenIddictUI.csproj ./src/OpenIddictUI/
COPY src/OpenIddictUI.Api/OpenIddictUI.Api.csproj ./src/OpenIddictUI.Api/
RUN dotnet restore src/OpenIddictUI/OpenIddictUI.csproj

COPY src/ ./src/
COPY --from=frontend /app/src/OpenIddictUI/wwwroot ./src/OpenIddictUI/wwwroot
RUN dotnet publish src/OpenIddictUI/OpenIddictUI.csproj -c Release -o /out --no-restore
RUN rm -rf /out/appsettings.Development.json

# stage 3: 运行时
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV LANG zh_CN.UTF-8
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
RUN apt-get update &&\
    apt-get install -y fontconfig iputils-ping net-tools curl && apt-get clean
COPY docker-entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/docker-entrypoint.sh
COPY TimesNewRoman.ttf /usr/share/fonts/truetype/deng/
COPY --from=backend /out ./
ENTRYPOINT ["docker-entrypoint.sh"]
CMD ["dotnet", "OpenIddictUI.dll"]
