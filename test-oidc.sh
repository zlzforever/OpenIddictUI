#!/bin/bash
# 手动测试 OAuth 2.0 Authorization Code + PKCE 完整流程
# 用法: chmod +x test-oidc.sh && ./test-oidc.sh

set -e

# =============== 配置 ===============
ISSUER="${ISSUER:-https://sample.ptkj.cc/openid}"
CLIENT_ID="${CLIENT_ID:-sample-app}"
REDIRECT_URI="${REDIRECT_URI:-https://sample.ptkj.cc/wildgoose/signin-oidc}"
SCOPE="${SCOPE:-openid profile offline_access}"
USERNAME="${USERNAME:-admin}"
PASSWORD="${PASSWORD:-}"

# =============== ① 生成 PKCE ===============
CODE_VERIFIER=$(openssl rand -hex 43)
CODE_CHALLENGE=$(echo -n "$CODE_VERIFIER" | openssl dgst -sha256 -binary | base64 | tr '/+' '_-' | tr -d '=' | tr -d '\n')
STATE="test-state-$(date +%s)"

REDIRECT_ENCODED=$(python3 -c "import urllib.parse; print(urllib.parse.quote('${REDIRECT_URI}', safe=''))")
SCOPE_ENCODED=$(python3 -c "import urllib.parse; print(urllib.parse.quote('${SCOPE}', safe=''))")

echo "=============================================="
echo "Step 1: 打开浏览器访问授权页面"
echo "=============================================="
AUTH_URL="${ISSUER}/connect/authorize?client_id=${CLIENT_ID}&redirect_uri=${REDIRECT_ENCODED}&response_type=code&scope=${SCOPE_ENCODED}&code_challenge=${CODE_CHALLENGE}&code_challenge_method=S256&state=${STATE}"

echo "$AUTH_URL"
echo ""
echo "复制上面的 URL 到浏览器打开，完成登录"
echo "登录后浏览器会重定向到 ${REDIRECT_URI}?code=xxx&state=xxx"
echo "把重定向后的完整 URL 粘贴到终端："
echo ""
read -r REDIRECTED_URL

# =============== ② 提取 code ===============
CODE=$(echo "$REDIRECTED_URL" | sed -n 's/.*code=\([^&]*\).*/\1/p')
RETURNED_STATE=$(echo "$REDIRECTED_URL" | sed -n 's/.*state=\([^&]*\).*/\1/p')

if [ -z "$CODE" ]; then
    echo "❌ 未能从 URL 中提取 authorization_code，请检查粘贴的 URL"
    exit 1
fi

if [ "$RETURNED_STATE" != "$STATE" ]; then
    echo "⚠️  State 不匹配！期望 $STATE，实际 $RETURNED_STATE（可能是 CSRF 攻击）"
fi

echo ""
echo "=============================================="
echo "Step 2: 用 authorization_code 换取 token"
echo "=============================================="

TOKEN_RESPONSE=$(curl -s -X POST "${ISSUER}/connect/token" \
    -d "grant_type=authorization_code" \
    -d "client_id=${CLIENT_ID}" \
    -d "code=${CODE}" \
    -d "code_verifier=${CODE_VERIFIER}" \
    -d "redirect_uri=${REDIRECT_URI}")

echo "$TOKEN_RESPONSE" | python3 -m json.tool 2>/dev/null || echo "$TOKEN_RESPONSE"

ACCESS_TOKEN=$(echo "$TOKEN_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('access_token',''))" 2>/dev/null)
REFRESH_TOKEN=$(echo "$TOKEN_RESPONSE" | python3 -c "import sys,json; print(json.load(sys.stdin).get('refresh_token',''))" 2>/dev/null)

if [ -z "$ACCESS_TOKEN" ]; then
    echo "❌ 未获取到 access_token，请检查上面的错误信息"
    exit 1
fi

echo ""
echo "✅ access_token: ${ACCESS_TOKEN:0:20}..."
echo "✅ refresh_token: ${REFRESH_TOKEN:0:20}..."

# =============== ③ 调用 userinfo ===============
echo ""
echo "=============================================="
echo "Step 3: 用 access_token 调用 userinfo"
echo "=============================================="

curl -s "${ISSUER}/connect/userinfo" -H "Authorization: Bearer ${ACCESS_TOKEN}" | python3 -m json.tool 2>/dev/null

echo ""
echo "=============================================="
echo "✅ 流程测试完成"
echo "=============================================="
