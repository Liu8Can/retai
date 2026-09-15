# retai 开发路线图

> 本文档基于对原仓库 [Planshit/Tai](https://github.com/Planshit/Tai) 274 条 Issue 的分类梳理、以及对当前代码库的架构评估，制定 retai 分支的维护与演进计划。

## 核心原则

1. **先稳后进**：在重构之前，先修复影响核心可信度的 Bug（统计不准、崩溃、数据库锁）
2. **测试先行**：任何重构必须有回归测试保护网，禁止无测试裸奔重构
3. **渐进迁移**：.NET Framework → .NET 8 分阶段推进，每阶段可独立发布，不做 big-bang 重写
4. **不破坏用户数据**：数据库 schema 变更必须兼容旧数据，提供迁移路径

---

## 阶段规划

### Phase 0：工程基础设施（当前阶段）

目标：建立开发与发布的工程基座，不改变运行时行为。

| 任务 | 优先级 | 说明 |
|------|--------|------|
| 建立 Git 分支规范与 Issue/PR 模板 | 高 | feat/ fix/ refactor/ 分支命名，Conventional Commits |
| 搭建 CI（GitHub Actions） | 高 | Windows runner 上 MSBuild 编译验证，确保 PR 不破坏构建 |
| 引入 xUnit 测试项目 | 高 | 新建 `Core.Tests` 项目，先覆盖纯逻辑（过滤、分类匹配、计时） |
| 原始 SQL 参数化 | 高 | Data.cs / WebData.cs 数十处字符串拼接 SQL 改为参数化查询 |
| 清理死代码与误引用 | 中 | IAppManager.cs 误引用 NPOI、注释掉的旧 SQL 等 |

### Phase 1：核心 Bug 修复（高价值，面向用户信任）

目标：修复动摇软件核心可信度的 Bug，让用户敢用、数据可信。

| Issue | 标题 | 根因方向 | 修复策略 |
|-------|------|---------|---------|
| #223 | database is locked 崩溃 | SQLite 并发写锁 + Thread.Sleep busy-wait | 改用 WAL 模式 + 优化连接生命周期 |
| #407 | 一天统计出 30 小时 | 计时累加逻辑缺陷，跨日/跨时段重复计算 | 审查 AppTimerServicer 计时路径，补边界测试 |
| #309 | 1 小时统计出 90 分钟 | 同上，计时精度问题 | 同上 |
| #412 | 锁屏仍记录使用时间 | Sleepdiscover 离开检测失效 | 审查睡眠检测触发条件 |
| #266 | 打开月/年详细统计崩溃 | 统计聚合查询 + 渲染 | 审查 Data.cs 聚合 SQL 和 ChartPageVM |
| #376 | 创建分组崩溃 | 分类创建路径空值 | 补空值保护 + 单元测试 |
| #372 | 右键菜单二级菜单关闭过快 | WPF 子菜单超时 | 调整 Popup 动画/延时 |
| #392 | 窗口大小不保留 | 设置持久化遗漏 | 审查窗口状态保存逻辑 |

### Phase 2：浏览器扩展修复（独立功能线）

目标：恢复网站浏览统计功能，当前几乎全链路不可用。

| Issue | 标题 | 说明 |
|-------|------|------|
| #397 | 浏览器扩展无法使用 | 扩展与 Core WebSocket 通信断链 |
| #388 | 扩展无法连接服务器 | WebServer 连接握手失败 |
| #327 | 网站统计时间一直为 0 | 数据接收但未正确入库或计时 |
| #205 | 支持国内浏览器 | 扩展兼容 Edge/国内 Chromium 浏览器 |

> 浏览器扩展是独立的前端项目（WebExtensions/），与 Core 通过 WebSocket 通信，可独立于 Core 重构修复。

### Phase 3：高价值功能增强

目标：在稳定基础上补齐同类软件标准能力。

| Issue | 功能 | 价值 | 复杂度 |
|-------|------|------|--------|
| #169 | 自定义每天起始时间 | 影响所有日/周统计口径，夜间工作者刚需 | 低-中 |
| #267 | 跨年总时长统计 | 突破当前年度限制 | 低 |
| #125 | 程序名通配符自动分类 | 降低分类维护成本 | 中 |
| #131 | 快速切换日期按钮 | 交互体验提升 | 低 |
| #243 | 时间线视图 | 同类软件标准能力 | 高 |

### Phase 4：技术栈现代化（.NET 8 迁移）

目标：将项目从 .NET Framework 4.8 迁移到 .NET 8 LTS，解决长期维护问题。

> 这是最大的工程投入，分 5 个子阶段推进。**必须在 Phase 0（测试）和 Phase 1（核心 Bug 修复）完成后启动**，否则没有回归保护网的迁移是赌博。

#### 4.1 数据访问层重写（EF6 → EF Core）
- 新建 EF Core 版 `TaiDbContext`，实体 POCO 可基本复用
- 用 EF Core Migrations 替代 `SQLiteBuilder.cs`（514 行手写 schema diff）
- `Microsoft.Data.Sqlite` 替代 `System.Data.SQLite.EF6`
- Data.cs / WebData.cs 原生 SQL 改为 LINQ 或 `FromSqlInterpolated`
- **数据迁移兼容**：确保旧 data.db 可被新 DbContext 正确读取

#### 4.2 csproj 现代化
- 四个项目转 SDK 风格 csproj
- `packages.config` → `PackageReference`
- 目标框架 `net8.0-windows`

#### 4.3 互操作校验
- 66 处 P/Invoke 逐一验证签名/字符集
- `WebSocketSharp`（停更预发布版）→ `System.Net.WebSponses`（内置）
- `TaskScheduler` 库升级到 .NET 8 兼容版本
- WinForms 互操作（NotifyIcon、ColorDialog）加 `<UseWindowsForms>true</UseWindowsForms>`

#### 4.4 UI 层迁移
- WPF 控件库验证（自绘 Charts 控件在新渲染栈表现）
- 主题系统验证

#### 4.5 依赖清理
- Newtonsoft.Json → System.Text.Json（可选，低优先级）
- NPOI 升级或替换
- 移除 BouncyCastle（若不再需要）

### Phase 5：架构改善（持续进行）

| 任务 | 说明 | 时机 |
|------|------|------|
| 拆分 Main 上帝服务 | 将启动编排、进程过滤、分类、网站数据落地分离 | Phase 1 后 |
| 拆分 Data / WebData | 引入 Repository 抽象，分离 CRUD 与统计聚合 | Phase 4.1 后 |
| 静态依赖抽象 | Iconer、SystemCommon、WebSocketEvent 等静态调用包装为可注入接口 | 随重构推进 |
| async void 消除 | Main.Run()、Sleepdiscover.Timer_Tick 改为 async Task | 随重构推进 |

---

## Release 策略

### 版本号

采用 `语义化版本（SemVer）`：`MAJOR.MINOR.PATCH`

- **0.x.x**：当前阶段，表示仍在整理期，API/数据格式可能变动
- **1.0.0**：Phase 1 完成，核心 Bug 修复完毕、测试基线建立，标记为首个稳定版
- **2.0.0**：Phase 4 完成，.NET 8 迁移落地

### 发布节奏

| 阶段 | 版本范围 | 发布内容 | 发布方式 |
|------|---------|---------|---------|
| Phase 0-1 | 0.1.0 - 0.5.0 | 工程 infra + 核心 Bug 修复 | GitHub Release，zip 包 |
| Phase 2-3 | 0.6.0 - 0.9.0 | 浏览器扩展修复 + 功能增强 | GitHub Release，zip 包 |
| Phase 4 | 1.0.0+ | .NET 8 迁移完成 | GitHub Release + 可选自动更新 |

### Release 产物

每次 Release 包含：
- `retai-win-x64-x.x.x.zip`（主程序）
- `retai-web-ext-x.x.x.zip`（浏览器扩展，如有改动）
- `SHA256SUMS.txt`（校验文件）
- Release Notes（关联修复的 Issue 编号）

### 自动更新

当前 Updater 项目从 GitHub Release 拉取更新。retai 继续使用此机制，Update 检查地址指向 `Liu8Can/retai` releases。需修改 Updater 中的仓库地址硬编码。

---

## 当前优先级排序

```
Phase 0（工程基座）
  ├─ 1. CI 搭建（编译验证）
  ├─ 2. 测试项目引入（纯逻辑覆盖）
  └─ 3. 原始 SQL 参数化（安全 + 为 EF Core 迁移铺路）
Phase 1（核心 Bug）
  ├─ 4. #223 数据库锁崩溃
  ├─ 5. #407/#309 统计时长错误
  ├─ 6. #412 锁屏误统计
  └─ 7. #266 月/年统计崩溃
Phase 2（浏览器扩展）
  └─ 8. #397/#388/#327 扩展通信修复
```

Phase 4（.NET 8 迁移）是长期目标，不急于启动。先把 Phase 0-1 做扎实。
