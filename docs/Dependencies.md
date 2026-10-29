# 依赖与许可证

本版本没有 NuGet/npm 运行依赖，不在运行时下载或执行第三方代码。没有附加字体、图片、图标或第三方静态资源；界面使用 Windows 系统字体。

| 项目 | 用途 | 本机验证/目标 | 许可证与部署 |
|---|---|---|---|
| .NET Framework | WPF、UIA、JSON、TCP | 目标 4.8.1；Runtime Release 533509 | Microsoft 系统组件，按 Windows/.NET 条款使用，不随应用复制框架 |
| Windows PowerShell | STA 宿主 | 5.1 | Windows 系统组件 |
| Microsoft Edge | 设备内 Translator API | 本机 155.0.4283.62；要求 148+ | Microsoft Edge 及模型适用条款，由用户/组织许可，不分发 Edge 或模型 |
| Windows Live Captions | 本地语音转文字 | Windows 11 | Windows 功能与语言资源，不分发系统二进制 |
| Visual Studio Build Tools | 开发编译 | VS 2022 17.14；MSBuild 17.14.60 | Microsoft Visual Studio Build Tools 条款，仅开发端 |
| VS 18 Community | 可选开发工具 | 本机也已安装；不是用户依赖 | 是否适合组织使用须独立遵守 Community 许可 |
| Node.js | 开发测试 | 使用内置 node:test；要求 20+ | Node.js MIT 及其第三方通知，仅开发端，不打包 |

C# 使用随 MSBuild 提供的编译器，语言版本固定 7.3，目标固定 .NET Framework 4.8.1。浏览器 JS 不经 npm 打包，直接使用 Edge 原生接口。项目仅交付 LCET.dll 和自有资源，不分发框架/浏览器许可证覆盖的二进制。

没有为项目源码擅自添加开源许可证。若后续引入受控依赖，必须在这里补充固定版本、来源、许可证和随附通知。

官方 API 边界参考：<https://learn.microsoft.com/en-us/microsoft-edge/web-platform/translator-api>。