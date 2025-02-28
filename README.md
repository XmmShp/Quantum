# Quantum - 模块化桌面应用框架

[![.NET 9](https://img.shields.io/badge/.NET-9-512BD4)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WebAssembly-blue)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![Build Test Status](https://github.com/XmmShp/Quantum/actions/workflows/build-test.yml/badge.svg)](https://github.com/XmmShp/Quantum/actions/workflows/build-test.yml)
[![Api Deployment Status](https://github.com/XmmShp/Quantum/actions/workflows/deploy-api-document.yml/badge.svg)](https://xmmshp.github.io/Quantum/)
[![Publish Status](https://github.com/XmmShp/Quantum/actions/workflows/publish.yml/badge.svg)](https://github.com/XmmShp/Quantum/releases/latest)

## 🌟 项目简介

一个基于.NET 9和Blazor构建的现代化桌面应用框架，采用模块化架构设计。通过内置的模块管理器，用户可以轻松安装/卸载功能模块，开发者可以快速创建可扩展的桌面应用程序。

**核心特性:**
- 🧩 模块化架构 - 功能按模块动态加载
- 📦 内置模块管理器 - 支持模块的安装/卸载/更新
- 🚀 跨平台 - 支持Windows/macOS/Linux

## ⚡ 快速开始

### 前置条件
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js](https://nodejs.org/en/download/)（依赖传递自 [Electron.NET](https://github.com/ElectronNET/Electron.NET)）
- ElectronNET.CLI
    ```
    dotnet tool install ElectronNET.CLI -g
    ```

### 运行程序
```bash
# 克隆仓库
git clone https://github.com/XmmShp/Quantum.git

# 进入Quantum.Runtime运行目录
cd Quantum
cd Quantum.Runtime

# 启动项目

## 以调试方式启动Quantum
electronize start /PublishSingleFile false /dotnet-configuration Debug

## 以发布方式启动Quantum
electronize start /PublishSingleFile false /dotnet-configuration Release

## 将项目打包为可安装程序
electronize build /PublishSingleFile false /target [win|osx|linux] 
```

## 🚧 项目路线图
- [ ] 模块市场
- [ ] 自动更新系统
- [ ] 主题/皮肤支持

## 🤝 贡献指南
欢迎通过以下方式参与贡献：
1. Fork仓库并创建特性分支
2. 提交Pull Request
3. 遵循[代码规范](CONTRIBUTING.md)
4. 使用有意义的commit message

## 📄 许可证
MIT License - 详见 [LICENSE](LICENSE)