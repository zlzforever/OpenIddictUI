/**
 * 动态获取当前项目部署的二级目录基础路径
 * 例1：部署在 https://xxx.com/abc/ → 返回 /abc/
 * 例2：部署在 https://xxx.com/ → 返回 /
 * 例3：部署在 https://xxx.com/abc/def/ → 返回 /abc/def/
 */
export function getBasePath(): string {
    debugger
    if (window.DEPLOY_BASE) {
        return window.DEPLOY_BASE;
    }
    // 默认根路径部署，不加任何前缀
    return '/'
}

export const BASE_PATH = getBasePath()