# PowerShell Plus - AI 增强终端

一个带有 AI 助手的 PowerShell 终端增强工具。用户可以用自然语言描述任务，AI 自动生成 PowerShell 命令。

![screenshot](screenshot.png)

## ✨ 功能特性

- **🤖 AI 命令生成**：输入自然语言描述，AI 自动生成对应的 PowerShell 命令
- **👀 命令预览**：执行前查看和编辑生成的命令，确保安全
- **⚡ 快捷命令**：常用命令一键执行
- **🔧 API 兼容**：支持 OpenAI 及兼容 API（DeepSeek、智谱、Moonshot 等）
- **🎨 现代 UI**：深色主题，与 Windows Terminal 风格一致

## 🚀 快速开始

### 环境要求

- Windows 10/11
- .NET 9.0 SDK

### 编译运行

```powershell
# 克隆项目
cd D:\xzh\AI_project\PowershellPlus

# 编译
dotnet build src/PowerShellPlus

# 运行
dotnet run --project src/PowerShellPlus
```

### 配置 API

1. 启动程序后，点击右上角 **⚙️ 设置** 按钮
2. 输入你的 API Key
3. 设置 API Base URL（默认为 OpenAI，可修改为其他兼容 API）
4. 选择模型名称
5. 点击保存

#### 支持的 API 服务

| 服务商 | API Base URL | 模型示例 |
|--------|-------------|---------|
| OpenAI | https://api.openai.com/v1 | gpt-4o-mini, gpt-4 |
| DeepSeek | https://api.deepseek.com/v1 | deepseek-chat |
| 智谱 AI | https://open.bigmodel.cn/api/paas/v4 | glm-4 |
| Moonshot | https://api.moonshot.cn/v1 | moonshot-v1-8k |

## 📖 使用说明

### AI 命令生成

1. 在右侧 AI 面板底部输入框输入自然语言描述
2. 按 Enter 或点击发送按钮
3. AI 分析后在"命令预览区"显示生成的命令
4. 确认无误后点击 **▶ 执行命令**

### 示例输入

- "列出当前目录下所有大于 100MB 的文件"
- "查找 D 盘中所有 .mp4 视频文件"
- "显示系统内存使用情况"
- "压缩 Documents 文件夹"
- "查看最近修改的 10 个文件"

### 快捷命令

点击"常用命令"区域的按钮可一键执行预设命令：
- 💻 系统信息
- 💾 磁盘空间
- 🌐 网络状态
- 📊 进程列表
- 🧹 清空屏幕

### 直接执行命令

在左侧终端底部的输入框可以直接输入 PowerShell 命令执行。

## 🏗️ 项目结构

```
PowerShellPlus/
├── src/PowerShellPlus/
│   ├── Models/           # 数据模型
│   ├── Services/         # 服务层 (AI, PowerShell)
│   ├── ViewModels/       # MVVM ViewModel
│   ├── Views/            # 窗口和控件
│   ├── Converters/       # 值转换器
│   ├── Resources/        # 样式资源
│   └── MainWindow.xaml   # 主窗口
└── README.md
```

## ⚠️ 安全提示

- AI 生成的命令请务必在执行前仔细审查
- 不要在命令中包含敏感信息
- 对于删除、格式化等危险操作请格外谨慎

## 📝 开发说明

### 技术栈

- **框架**: WPF (.NET 9)
- **MVVM**: CommunityToolkit.Mvvm
- **PowerShell**: Microsoft.PowerShell.SDK

### 添加自定义命令

编辑 `%APPDATA%\PowerShellPlus\settings.json` 文件：

```json
{
  "customCommands": [
    {
      "name": "我的命令",
      "command": "Get-Process",
      "icon": "🔧",
      "description": "命令描述"
    }
  ]
}
```

## 📄 License

MIT License

