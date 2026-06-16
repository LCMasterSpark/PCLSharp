# Plain Craft Launcher Sharp

**PCL Sharp | PCL#**

> ⚠️ **这不是官方 Plain Craft Launcher，也不是龙腾猫跃（Hex-Dragon）的项目。这是第三方独立重构版。**
> This is NOT the official Plain Craft Launcher, nor is it Hex-Dragon's project. This is an independent third-party rebuild.

![Version](https://img.shields.io/badge/version-v0.6pre-512BD4?style=flat-square)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)
![Platform](https://img.shields.io/badge/platform-Windows-0078D4?style=flat-square)
![UI](https://img.shields.io/badge/UI-WPF%20%2F%20MVVM-68217A?style=flat-square)

---

## 简介 · Overview

**Plain Craft Launcher Sharp** 是一款 Windows Minecraft 启动器，由 **LCMasterSpark** 以 C#（WPF / MVVM 架构）从零重新实现，目标是功能上一比一还原原版 [Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2)，同时在内部架构上采用更现代、更稳定、更可测试的 C# 实现。

PCL Sharp is a Windows Minecraft launcher reimplemented from scratch in C# (WPF / MVVM), aiming to match the feature set of the original [Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2) while adopting a modern, testable, service-oriented architecture.

> **当前状态 · Current status**：Pre-release v0.6pre — 核心功能逐步补齐中，尚未达到日常稳定使用。
> Core functionality is being actively completed; not yet stable for daily use.

---

## 功能 · Features

| 模块 Module | 状态 Status | 说明 Notes |
|-------------|-------------|------------|
| **启动管线 · Launch Pipeline** | ✅ ~90% | Java 发现/选择、多方式登录、参数生成、文件补全、Natives 提取、补丁、GPU 偏好、进程管理、崩溃诊断 |
| **下载系统 · Download System** | ✅ ~85% | 原版游戏下载、Fabric/Quilt/Forge/NeoForge 安装、Modrinth/CurseForge 搜索、整合包安装与导出 |
| **实例管理 · Instance Management** | ✅ ~85% | 实例扫描、版本链解析、本地 Mod 管理、实例级启动设置、导出整合包 |
| **联机 · Multiplayer Link** | 🔶 ~50% | Terracotta / EasyTier 双后端框架、进程启停、端口分配、独立日志；待补二进制下载与节点配置 |
| **设置系统 · Settings** | ✅ ~90% | 全局设置、UI 外观、Minecraft 路径、下载、启动、联机、调试选项 |
| **更多页 · More** | 🟡 ~70% | 帮助系统、诊断工具、功能枢纽（更新检查、崩溃分析、账号中心、皮肤中心、扩展点） |

---

## 架构 · Architecture

```
PCLrmkBYCSharp/
├── Models/          # 数据模型（纯对象）
├── Services/        # 业务逻辑层（接口 + 实现，依赖注入）
│   ├── Launch/      # 启动管线：15 步编排
│   ├── Downloads/   # 下载系统：队列、搜索、安装
│   ├── Link/        # 联机系统：Terracotta / EasyTier
│   └── FeatureHub/  # 功能枢纽：诊断、更新、扩展
├── ViewModels/      # MVVM 视图模型（CommunityToolkit.Mvvm）
├── Views/           # WPF XAML 页面
└── Resources/       # 图片、补丁、帮助文档
```

**技术栈 · Stack**: .NET 10.0 / WPF / CommunityToolkit.Mvvm / xUnit

---

## 构建与测试 · Build & Test

```powershell
cd E:\PCLrmkBYC#
dotnet build       # 0 警告 0 错误
dotnet test        # 544 tests, all passing
```

外部依赖：仅 `CommunityToolkit.Mvvm`（NuGet）。旧 VB 原版项目仅作参考，不构成依赖。

---

## 与原版的关系 · Relation to Original PCL

本项目是**第三方独立二次开发**产物，基于原版 [Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2)（作者：龙腾猫跃 · Hex-Dragon）的功能规格和交互设计参考实现。

This project is an **independent third-party reimplementation** based on the functional spec and interaction design of the original [Plain Craft Launcher 2](https://github.com/Hex-Dragon/PCL2) by Hex-Dragon (龙腾猫跃).

- ✅ 功能目标：一比一还原原版 PCL
- ✅ 内部架构：MVVM、服务拆分、异步模型、测试体系 — 与原版完全不同
- ❌ 非官方：这不是原版 PCL 的正式分支、续作或替代品

---

## 许可协议 · License

本项目**不采用 MIT、GPL 等通用开源协议**。本项目遵循原版 Plain Craft Launcher 2 的开源指南（以下简称"原指南"）进行发布：

This project does NOT use MIT, GPL, or any common open-source license. It is released under the original Plain Craft Launcher 2 Open Source Guidelines (hereinafter "the Guidelines").

### 原指南核心要求

1. **署名** — 在关于页面首位给出龙腾猫跃（Hex-Dragon）的署名及赞助链接：[https://afdian.com/a/LTCat](https://afdian.com/a/LTCat)
2. **公开源代码** — 本存储库始终保持公开
3. **名称** — 以 `Plain Craft Launcher` 开头并附加后缀 `Sharp`，以表明这是第三方修改版
4. **不混淆** — 明确标注这不是官方版本，不与原版 PCL 混淆
5. **正版劝导** — 软件保留 Minecraft 正版购买劝导机制

完整原指南请参阅：[PCL 开源指南 · 原版仓库](https://github.com/Hex-Dragon/PCL2)

**法律声明**：在法律上，原版 PCL 保留所有权利（All Rights Reserved）。本指南不是正式法律文件或协议。若您参考或使用了本项目的代码，请同样遵守上述原则。

---

## 致谢 · Acknowledgements

- **龙腾猫跃（Hex-Dragon）** — 原版 Plain Craft Launcher 的长期开发与公开项目参考
- **bangbang93** — BMCLAPI 镜像源和生态支持
- **MC 百科 · MCmod** — Mod 名称与资料索引
- **EasyTier** — 联机模块的参考方向
- 所有参与测试与反馈的朋友们

---

## 项目地址 · Repositories

- 原版 PCL（Original）：[github.com/Hex-Dragon/PCL2](https://github.com/Hex-Dragon/PCL2)
- PCL Sharp（本仓库）：[github.com/LCMasterSpark/PCLSharp](https://github.com/LCMasterSpark/PCLSharp)
