# PowerShell Plus - AI 增强终端

一个带有 AI 助手的 PowerShell 终端增强工具。基于 Windows ConPTY 技术实现原生终端体验，用户可以用自然语言描述任务，AI 自动生成 PowerShell 命令。

![screenshot](screenshot.png)

## ✨ 功能特性

- **⚡ 原生终端体验**：基于 Windows ConPTY 伪终端技术，提供完整的 PowerShell 交互体验
- **🖥️ 现代终端渲染**：使用 WebView2 + xterm.js 实现，支持颜色、光标、特殊字符等完整终端功能
- **🤖 AI 命令生成**：输入自然语言描述，AI 自动生成对应的 PowerShell 命令
- **👀 命令预览**：执行前查看和编辑生成的命令，确保安全
- **⭐ 快捷命令管理**：支持添加、编辑、删除自定义快捷命令
- **🔧 API 兼容**：支持 OpenAI 及兼容 API（DeepSeek、智谱、Moonshot 等）
- **🎨 深色主题**：与 Windows Terminal 风格一致的现代 UI
- **🌐 UTF-8 支持**：自动配置 UTF-8 编码，优先使用 PowerShell 7+

## 🚀 快速开始

### 环境要求

- Windows 10/11
- .NET 9.0 SDK
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Windows 11 已内置）

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

### 终端操作

- **直接输入命令**：点击左侧终端区域，直接输入 PowerShell 命令
- **中断命令**：点击底部 **⏹ 中断** 按钮发送 Ctrl+C
- **清屏**：点击 **🧹 清屏** 按钮清空终端

### AI 命令生成

1. 在右侧 AI 面板底部输入框输入自然语言描述
2. 按 Enter 或点击发送按钮
3. AI 分析后在"命令预览区"显示生成的命令
4. 可直接编辑命令进行调整
5. 确认无误后点击 **▶ 执行命令**

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
- 📁 目录列表
- 🐍 Conda 环境

点击 **⚙️** 按钮可管理快捷命令：添加、编辑、删除或恢复默认。

## 🏗️ 项目结构

```
PowerShellPlus/
├── src/PowerShellPlus/
│   ├── Controls/         # 自定义控件 (WebTerminalControl)
│   ├── Converters/       # 值转换器
│   ├── Models/           # 数据模型
│   ├── Resources/        # 样式资源和终端 HTML
│   │   ├── Styles.xaml
│   │   └── Terminal/
│   │       └── terminal.html
│   ├── Services/         # 服务层
│   │   ├── ConPtyService.cs      # Windows ConPTY 伪终端
│   │   ├── OpenAIService.cs      # AI API 调用
│   │   └── ...
│   ├── ViewModels/       # MVVM ViewModel
│   ├── Views/            # 窗口和对话框
│   │   ├── SettingsWindow.xaml
│   │   └── CommandManagerWindow.xaml
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
- **终端渲染**: Microsoft.Web.WebView2 + xterm.js
- **伪终端**: Windows ConPTY API
- **PowerShell SDK**: Microsoft.PowerShell.SDK

### 核心实现

- **ConPtyService**: 封装 Windows ConPTY API，创建真正的伪终端进程
- **WebTerminalControl**: 使用 WebView2 加载 xterm.js，提供现代终端 UI
- **OpenAIService**: 调用 AI API 生成 PowerShell 命令

### 添加自定义命令

可通过界面管理，或直接编辑 `%APPDATA%\PowerShellPlus\settings.json` 文件：

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
