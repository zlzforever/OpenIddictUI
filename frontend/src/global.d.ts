declare global {
    interface Window {
        DEPLOY_BASE?: string; // 允许 undefined / 字符串
    }
}

export {};