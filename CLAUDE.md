# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目简介

AssetStudio 是一个 Unity 资源提取工具，支持 Unity 2.x 到 Unity 6 (6000.x)，将游戏中的纹理、模型、音频、着色器等资源导出为标准格式。提供 GUI 和 CLI 两种界面。项目从 Perfare/AssetStudio 和 RazTools/Studio 演化而来，在上游暂停维护后独立继续开发。

## 构建命令

```powershell
# 完整构建
dotnet build AssetStudio.sln -c Release

# 仅构建 CLI
dotnet build AssetStudio.CLI/AssetStudio.CLI.csproj -c Release

# 仅构建 GUI
dotnet build AssetStudio.GUI/AssetStudio.GUI.csproj -c Release

# 指定目标框架
dotnet build AssetStudio.CLI/AssetStudio.CLI.csproj -c Release -f net10.0-windows

# 清理
dotnet clean AssetStudio.sln
```

项目同时支持 net8.0 和 net10.0 两个目标框架。输出路径：`bin/Release/net10.0-windows/`。

## 发布流程

1. 更新 `VERSION` 文件
2. 提交并打标签：`git tag v2.x.x`，推送标签触发 GitHub Actions 自动构建发布

## 架构概览

```
数据流：文件输入 → AssetsManager.LoadFiles()
  → 并行 Bundle 解压 (BundleFile.cs)
  → SerializedFile.Parse() + TypeTree 解析
  → 对象反序列化 (Classes/*.cs)
  → GUI展示 / 并行导出 (Exporter.cs)
  → 输出文件 (PNG, FBX, OBJ 等)
```

### 项目结构与职责

| 项目 | 职责 |
|---|---|
| **AssetStudio/** | 核心库 - 资源解析逻辑、二进制读取、压缩解压、加密处理 |
| **AssetStudio.CLI/** | 命令行界面 - Program.cs 入口, Exporter.cs 批量导出 |
| **AssetStudio.GUI/** | Windows Forms GUI - MainForm.cs 主窗口, Studio.cs GUI编排 |
| **AssetStudio.Utility/** | 导出工具 - 各格式转换器 (Texture2D, Model, Audio, Shader) |
| **AssetStudio.FBXWrapper/** | FBX 导出 C# 封装 |
| **AssetStudio.FBXNative/** | FBX 原生 C++ 库 (vcxproj) |
| **AssetStudio.PInvoke/** | 平台调用工具 |

### 核心类

- **AssetsManager.cs** - 主编排器，负责文件加载、并行处理、依赖解析。使用 `Task.Run` 和 `Parallel.ForEach` 实现多线程
- **SerializedFile.cs** - 底层 Unity 资源文件解析器，读取文件头、TypeTree、对象表
- **ObjectReader.cs** - 带 Unity 版本上下文的 EndianBinaryReader 封装
- **BundleFile.cs** - Unity Bundle 解压缩
- **EndianBinaryReader.cs** - 二进制读取器，`ReadAlignedString()` 必须始终调用 `AlignStream()`
- **Classes/*.cs** - 各 Unity 资源类型的反序列化类 (Shader, Texture2D, Mesh 等)

## 关键约定

### 多线程安全

项目大量使用多线程（Bundle 解压、资源加载、对象反序列化、资产导出全部并行）。修改时必须：
- 对共享集合的写操作加锁 (`assetsFileListLock`, `importFilesLock`)
- 使用 `ConcurrentDictionary` 管理资源读取器
- Logger 必须线程安全
- 对象构造函数应为只读操作

### Unity 版本检测

版本存储为 `int[]`，如 Unity 6000.0.58f2 → `[6000, 0, 58, 2]`。`reader.version[3]` 中 0=a, 1=b, 2=f, 3=p。

```csharp
if (version[0] >= 6000) { /* Unity 6+ */ }
if (version[0] > 2020 || (version[0] == 2020 && version[1] >= 2)) { /* 2020.2+ */ }
```

### 反序列化模式

两种模式：
1. **基于类**（优先）- 手写 C# 类，快速、类型安全，需随 Unity 格式变化更新
2. **基于 TypeTree**（回退）- 泛型字典反序列化，用于未知类或 MonoBehaviour 脚本

### 错误处理哲学

**优雅降级优于崩溃**：部分数据 > 完全失败。在反序列化器中使用 try-catch 继续加载，使用 Logger 的适当级别记录：
- `Logger.Error()` - 严重失败（文件损坏）
- `Logger.Warning()` - 解析失败但可恢复
- `Logger.Verbose()` - 格式差异、调试信息

### 代码命名规范

- 类名 PascalCase，字段用 `m_` 前缀，属性 PascalCase
- 读取数组模式：先 `ReadInt32()` 获取 count，再循环读取
- 变长数据读取后调用 `reader.AlignStream()` 对齐到 4 字节边界

## 不可破坏的关键部分

- `EndianBinaryReader.ReadAlignedString()` - 必须始终调用 `AlignStream()`（v2.2.1 行为）
- `ObjectReader.Remaining` - 必须使用基类计算（基于流的，非对象作用域）
- `ObjectReader.Read()` 的边界检查 - 保持 v2.2.1 版本
- `Studio.cs` 中的 `Parallel.ForEach` - 多线程导出的关键
- Shader.cs 中 Unity 6000+ 的 shader 解析旁路 - 有意跳过未记录的格式

## 已知问题

- Unity 6 (6000.0.58f2) 约 1,082 个 shader 无法完全解析（序列化格式未公开文档），已用旁路跳过
- SkinnedMeshRenderer 有少量解析失败（约 191 个），与 CAB 修复无关
- MiHoYo 游戏使用加密 bundle（XOR）和自定义文件格式，通过 Game 枚举和 Crypto/ 目录处理

## 测试方式

无自动化测试。手动测试流程：加载 Unity 游戏文件到 AssetStudio GUI，检查控制台错误，导出资源验证有效性。常用测试游戏：原神 (Unity 2020.3)、Marvel Snap (Unity 6000.0)、Among Us (Unity 2019.4)。
