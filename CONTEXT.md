# Plain Craft Launcher Sharp — 领域上下文

## 项目定位

Plain Craft Launcher Sharp（简称 PCL Sharp / PCL#）是一个 Windows Minecraft 启动器，以 C# WPF MVVM 架构重新实现原版 Plain Craft Launcher 2 的功能集。

**核心约束**：功能一比一还原原版 PCL，内部架构可以大胆优化（服务拆分、MVVM、异步模型、测试覆盖、错误隔离）。

**非约束**：不依赖旧 VB 项目的任何代码或库，旧项目只作参考。

## 领域概念

| 术语 | 定义 |
|------|------|
| **启动管线** | 从 Java 发现 → 登录 → 参数生成 → 文件补全 → Natives → 预运行 → 补丁 → 自定义命令 → 脚本导出 → GPU 偏好 → 内存优化 → 进程启动 → 窗口处理 → 崩溃诊断 的 15 步流程，由 LaunchPipelineService 编排 |
| **实例** | 一个具体的 Minecraft 版本安装，包含版本 JSON、JAR、libraries、natives、Mod 文件和独立配置。对应磁盘上的 versions/<name>/ 目录 |
| **版本链** | 一个实例通过 inheritsFrom 字段指向父版本的继承链，可能是原版 → Fabric → 整合包的多层结构 |
| **根目录** | Minecraft 游戏根目录（.minecraft），包含 versions、libraries、assets、saves、mods 等子目录 |
| **登录方式** | 离线登录（Legacy）、微软账号（Microsoft / OAuth 设备码）、第三方 Yggdrasil（统一通行证 / LittleSkin / Authlib-Injector）|
| **下载系统** | 统一下载队列，支持原版游戏下载、社区资源搜索（Modrinth / CurseForge）、加载器安装（Fabric / Quilt / Forge / NeoForge）、整合包安装与导出 |
| **联机** | 基于 Terracotta 或 EasyTier 后端的多人游戏连接。Terracotta 是主入口，EasyTier 作为高级/底层模式保留 |
| **功能枢纽** | 更多页的实验与规划入口，包含更新系统、崩溃分析、主页公告、账号中心、皮肤中心、扩展点目录 |
| **加载器处理器** | 执行 Forge / NeoForge 的 Processor 链的独立进程 |

## 架构概览

### 分层

- **Models** — 纯数据对象，无行为
- **Services** — 业务逻辑，通过 I*Service 接口暴露
- **ViewModels** — 每页一个 ViewModel，使用 CommunityToolkit.Mvvm
- **Views** — XAML 页面，DataTemplate 绑定到 ViewModel 类型

### 关键服务

- AppServices — 应用程序服务容器，在 App 启动时创建所有服务实例
- NavigationService — 页面路由，创建 ViewModel 实例，管理页面切换
- LaunchPipelineService — 启动管线编排
- DownloadManagerService — 下载队列管理
- JavaDiscoveryService — 扫描系统上的 Java 安装
- LoginService — 统一登录入口，路由到 Legacy / Ms / Yggdrasil
- PclLinkService — 联机服务入口
- FeatureHubService — 功能枢纽（诊断、更新、扩展点）

### 启动流程

1. App.OnStartup → AppServices.Create → 创建所有服务 → MainWindow 显示
2. MainWindowViewModel 初始化页面 → 预加载下载版本清单
3. 用户选择实例 → 配置 Java / 登录 → 点击启动
4. LaunchPipelineService.BuildAndRunAsync → 15 步管线逐级推进
5. 游戏进程启动 → GameProcessWatcher 监控退出 → 诊断汇总

## 重要约定

- 下载任务统一经过 DownloadManagerService，支持暂停/恢复/校验/限速
- 设置以键值对存储在 AppSettingKeys 中，通过 IAppSettingsService 读写
- 实例级设置通过实例名前缀隔离（VersionAdvanceJvm 等）
- UI 跨线程操作通过 IUiDispatcherService.Invoke 封送
- 用户交互通过 IUserPromptService / IFileDialogService
- 日志通过 IAppLoggerService
- 默认主题为 VS2022Dark（黑紫暗色 + 侧边栏风格）
