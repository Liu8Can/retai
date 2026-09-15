# Tai Sentry — Firefox 扩展

网页浏览数据统计拓展，配合 retai（Tai）桌面程序使用。

## 安装

### 临时加载（开发调试）

1. 打开 Firefox，访问 `about:debugging#/runtime/this-firefox`
2. 点击「临时载入附加组件」
3. 选择本目录下的 `manifest.json` 文件

### 正式安装（打包签名）

```bash
# 安装 web-ext（一次性）
npm install --global web-ext

# 打包
web-ext build --source-dir . --artifacts-dir ./web-ext-artifacts
```

构建产物在 `web-ext-artifacts/` 目录下（已被 .gitignore 排除）。

## 使用

1. 确保 retai（Tai）桌面程序正在运行
2. 在 retai 的 设置 > 常规 > 功能 中启用「网站浏览统计」
3. 安装本扩展后，浏览器工具栏图标变为绿色表示已连接 Tai 服务器

## 与 Chrome 扩展的区别

| 差异 | Chrome | Firefox |
|------|--------|---------|
| Manifest 版本 | MV3 | MV2 |
| 背景 | service_worker | persistent script |
| 图标 API | chrome.action | chrome.browserAction |
| 最低版本 | Chrome 116 | Firefox 115+ |
