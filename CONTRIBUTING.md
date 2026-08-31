# 🤝 贡献指南

欢迎加入Quantum项目！以下是参与贡献的标准流程指南：

## 🚀 开始贡献

### 贡献方式
- 🐛 **报告Bug** - 使用[issues](https://github.com/XmmShp/Quantum/issues)功能
- 💡 **提议功能** - 使用[issues](https://github.com/XmmShp/Quantum/issues)功能
- 🔨 **代码贡献** - 通过[Pull Request](https://github.com/XmmShp/Quantum/pulls)提交代码
- 📚 **改进文档** -通过[Pull Request](https://github.com/XmmShp/Quantum/pulls)完善指南或注释

## 🔧 开发流程

### 分支策略
1. 从您Fork的仓库创建特性分支：
   ```bash
   git checkout -b feat/your-feature main
   ```
2. 提交原子化的commit：
   ```bash
   git commit -m "feat(plugin): 添加插件能力"
   ```

## 🔄 PR提交规范

### 创建PR
1. 确保通过所有测试：
   ```bash
   dotnet restore Quantum.slnx
   dotnet build Quantum.slnx --no-restore
   dotnet test quantum/tests/Quantum.Tests/Quantum.Tests.csproj --no-build
   dotnet test quantum-extension-market/tests/Quantum.ExtensionMarket.Tests/Quantum.ExtensionMarket.Tests.csproj --no-build
   ```
2. 更新文档（如有接口变更）：
   ```markdown
   ## API变更
   - `IQuantumPlugin` 或 `plugin.json` 的兼容性变化
   ```
3. 推送分支并创建Pull Request

### PR审查标准
- ✅ 通过持续集成测试
- ✅ 至少1位维护者批准
- ✅ 更新所有相关文档

## ⚖️ 许可协议
所有贡献将遵循项目的[MIT许可证](LICENSE)。提交PR即表示您同意遵守此协议。
