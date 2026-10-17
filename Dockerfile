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
# restore 指定RID，下载linux-x64 runtime pack（restore 忽略 /p:RuntimeIdentifier，必须用 -r）
RUN dotnet restore src/OpenIddictUI/OpenIddictUI.csproj -r linux-x64

COPY src/ ./src/
COPY --from=frontend /app/src/OpenIddictUI/wwwroot ./src/OpenIddictUI/wwwroot

# ✅ --self-contained true 完整写法
RUN dotnet publish src/OpenIddictUI/OpenIddictUI.csproj --self-contained true /p:RuntimeIdentifier=linux-x64 -c Release -o /out

RUN rm -rf /out/appsettings.Development.json
#RUN if [ -d /out/runtimes ]; then find /out/runtimes -mindepth 1 -maxdepth 1 ! -name 'linux-x64' -exec rm -rf {} +; fi

# stage 3 运行镜像
FROM debian:bookworm-slim
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# 阿里云 bookworm deb822 源（echo -e 单行方案，兼容所有docker版本）
RUN echo "deb http://mirrors.aliyun.com/debian bookworm main non-free-firmware" > /etc/apt/sources.list && \
    echo "deb http://mirrors.aliyun.com/debian bookworm-updates main non-free-firmware" >> /etc/apt/sources.list && \
    echo "deb http://mirrors.aliyun.com/debian-security bookworm-security main non-free-firmware" >> /etc/apt/sources.list && \
    rm -f /etc/apt/sources.list.d/debian.sources


# 安装依赖 + 中文locale + 字体工具
RUN apt-get update &&\
    apt-get install -y fontconfig locales libicu72 libssl3  &&\
    locale-gen zh_CN.UTF-8 && apt-get clean && rm -rf /var/lib/apt/lists/*

COPY docker-entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

# 创建字体目录，拷贝字体并刷新字体缓存
RUN mkdir -p /usr/share/fonts/truetype/deng
COPY TimesNewRoman.ttf /usr/share/fonts/truetype/deng/
RUN fc-cache -fv

# 创建appuser（需在COPY前创建，--chown才能引用用户名）
RUN adduser --disabled-password --gecos "" appuser

# 拷贝 .NET 自包含应用产物，拷贝时直接设置属主（避免 chown -R 复制整层，镜像体积翻倍）
COPY --chown=appuser:appuser --from=backend /out ./

# 切换到非root用户
USER appuser

ENTRYPOINT ["docker-entrypoint.sh"]
CMD ["./OpenIddictUI"]