# .NET 8 迁移方案

> 基于 Architect Agent 对代码库的全面评估，制定 .NET Framework 4.8 → .NET 8 迁移的 8 阶段执行计划。
> 总工作量估算：17-20 个工作日（3-4 周）。

## 可行性结论：有条件可行，无不可逾越的阻碍

## 阻碍点清单（按难度排序）

| # | 阻碍 | 文件 | 方案 | 难度 |
|---|------|------|------|------|
| 1 | SQLiteBuilder 手写 schema diff | SQLiteBuilder.cs (514行) | EF Core Migrations 替代 | 高 |
| 2 | EF6 → EF Core 数据访问层 | Data.cs, WebData.cs, Categorys.cs | FromSqlRaw/ExecuteSqlRaw + keyless 实体 | 中高 |
| 3 | Expression.Blend.Sdk → Microsoft.Xaml.Behaviors | 9 个 XAML 文件 | 包替换 + xmlns 全局替换 | 中 |
| 4 | System.Data.SQLite.EF6 提供程序 | SQLiteConfiguration.cs, App.config | 删除，用 UseSqlite() | 中 |
| 5 | Win32 P/Invoke (66处) | Win32API.cs, AppManager.cs | 零改动，完全兼容 | 低 |
| 6 | UIAutomationClient COM 引用（僵尸） | Core.csproj:232-240 | 直接删除 | 低 |
| 7 | 旧式 csproj → SDK 风格 | 4 个 csproj | 转换 + PackageReference | 中 |
| 8 | 第三方包升级 | packages.config | 逐个升级/替换 | 中 |

## 8 阶段执行计划

### P0：零成本清理 ✅ 当前阶段
- 删除 UIAutomationClient 僵尸 COM 引用
- 删除 WebData.cs 的无用 AxHost using
- 测试项目保持 net48（Core 迁移后才能转 net8）

### P1：Core.csproj 转 SDK 风格（仍 net48）
- 旧式 csproj → SDK 风格，TFM 保持 net4.8
- packages.config → PackageReference
- 验证编译 + 46 个测试全过

### P2：UI/TaiBug/Updater 转 SDK 风格（仍 net48）
- 三个 WPF 项目转 SDK 风格
- 保留 UseWPF/app.manifest/ApplicationIcon
- Expression.Blend.Sdk 暂留（net48 仍可用）

### P3：EF Core 数据访问层并行实现
- 新增 Microsoft.EntityFrameworkCore.Sqlite
- 新建 TaiDbContextCore（EF Core），复用现有 POCO 实体
- 编写 Initial Migration
- 新旧共存认证：同一份 data.db 分别跑 EF6 与 EF Core，结果集对比

### P4：Schema 自检机制迁移
- 用 Database.Migrate() 替代 SQLiteBuilder.SelfCheck()
- 保留 data.db.version 触发语义
- 用历史快照做迁移回归测试

### P5：Core 切换到 net8.0-windows
- TFM net48 → net8.0-windows
- 移除 EF6/System.Data.SQLite 全部包
- SQLiteConfiguration.cs 删除
- Database.Connection → GetDbConnection()
- 测试项目同步转 net8.0-windows

### P6：UI XAML Behaviors 迁移 + 切 net8.0-windows
- Expression.Blend.Sdk → Microsoft.Xaml.Behaviors.Wpf
- 9 个 XAML 的 xmlns:i → xmlns:b
- UI/TaiBug/Updater TFM → net8.0-windows
- 升级剩余第三方包

### P7：CI 切换 + 发布流程
- ci.yml/release.yml 全部改 dotnet build/dotnet test
- 移除 nuget restore/setup-msbuild
- 添加 dotnet format 验证

## 数据库迁移策略（5 层防护）

1. **Schema 完全冻结** — EF Core Initial Migration 精确还原旧库 schema，不改表名/列名
2. **历史快照回归** — 用 5+ 个不同版本的 data.db 做迁移回归测试
3. **保留版本文件触发** — 仅版本变化时执行 Migrate()
4. **强制备份** — 迁移前自动复制 data.db
5. **用户可见的回退路径** — 发布说明提供手动备份/回退指引

## 关键代码迁移要点

### TaiDbContext
- EF6: `DbConfiguration.SetConfiguration(new SQLiteConfiguration())` → EF Core: `UseSqlite()` 链式调用
- 必须显式 `ToTable("AppModels")` 等，因为 EF Core 默认用 DbSet 属性名，旧库用复数名
- `SQLiteBuilder.SelfCheck()` → `Database.Migrate()`

### Data.cs / WebData.cs 的 SqlQuery
- EF6 `Database.SqlQuery<T>` 可映射任意 DTO
- EF Core `FromSqlRaw<T>` 仅限实体类型
- 非实体 DTO 用 ADO.NET 直接读取 或 keyless 实体 `HasNoKey().ToView(null)`

### Win32 P/Invoke — 零改动
- 所有 DllImport 签名在 .NET 8 完全兼容
- COM [ComImport] 仍支持
- 可选后续优化: [LibraryImport] 源生成器
