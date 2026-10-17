#!/bin/bash

set -e

# 在这里添加你需要执行的命令
# 例如，运行数据库迁移、配置检查等
fc-cache -f -v

generate() {
    # 输入文件名
    input_file="$1"
    # 输出文件名
    output_file="$2"
    
    # 检查输入文件是否存在
    if [ -f "${input_file}" ]; then
       awk '{
           while (match($0, /\$\{[A-Za-z_][A-Za-z0-9_]*\}/)) {
               var = substr($0, RSTART + 2, RLENGTH - 3)
               # 只替换【当前匹配到的这一个】变量，而不是全部替换
               before = substr($0, 1, RSTART - 1)
               after = substr($0, RSTART + RLENGTH)
               $0 = before ENVIRON[var] after
           }
           print
       }' "$input_file" > "$output_file"
       echo "配置文件已生成"
    else
       echo "使用默认配置文件"
    fi
}

## api 配置文件
if [ -z "$CONFIG_SOURCE" ]; then
    echo "环境变量 CONFIG_SOURCE 不存在， 使用默认配置文件"
else
    generate "${CONFIG_SOURCE}" "/app/appsettings.json"
fi

# 修复 <base href>、HTML 入口资源路径和登录背景（只需修 HTML）
index_file="/app/wwwroot/index.html"
if [ ! -f "$index_file" ]; then
    echo "前端入口文件不存在：$index_file" >&2
    exit 1
fi

# BASE_PATH 统一为带前导和尾部斜杠的路径，例如 /openid/；根路径统一为 /。
base_path="${BASE_PATH:-}"
base_path="${base_path#/}"
base_path="${base_path%/}"
if [ -n "$base_path" ]; then
    normalized_base_path="/${base_path}/"
else
    normalized_base_path="/"
fi

# 转义 sed replacement 中可能有特殊含义的字符。
escaped_base_path=$(printf '%s' "$normalized_base_path" | sed 's/[\\&|]/\\&/g')
front_backend="${FRONT_BACKEND:-}"
escaped_front_backend=$(printf '%s' "$front_backend" | sed 's/[\\&|]/\\&/g')

# 支持 <base href="/">、<base href="/" />、<base href="/"/> 以及单引号写法。
sed -i -E \
    "s|(<base[[:space:]][^>]*href[[:space:]]*=[[:space:]]*)\"[^\"]*\"|\\1\"${escaped_base_path}\"|; \
     s|(<base[[:space:]][^>]*href[[:space:]]*=[[:space:]]*)'[^']*'|\\1\"${escaped_base_path}\"|" \
    "$index_file"

# Vite 可能输出 ./assets/，也可能因为上一次启动已经变成 /old-prefix/assets/；
# 统一成当前前缀，保证容器重启或 BASE_PATH 变更后仍然可用。
sed -i -E \
    "s|([\"'])\./assets/|\\1${escaped_base_path}assets/|g; \
     s|([\"'])/([^\"']*/)?assets/|\\1${escaped_base_path}assets/|g" \
    "$index_file"

# 将运行时配置的前端地址写入登录页背景配置；未配置时清空已有值。
sed -i -E \
    "s|(<meta[[:space:]]+name=[\"']login-bg[\"'][^>]*content[[:space:]]*=[[:space:]]*)\"[^\"]*\"|\\1\"${escaped_front_backend}\"|; \
     s|(<meta[[:space:]]+name=[\"']login-bg[\"'][^>]*content[[:space:]]*=[[:space:]]*)'[^']*'|\\1\"${escaped_front_backend}\"|" \
    "$index_file"

echo "已使用 BASE_PATH=${normalized_base_path}、FRONT_BACKEND=${front_backend} 配置前端入口"

exec "$@"
