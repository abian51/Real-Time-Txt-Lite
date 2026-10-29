# Real-Time-Txt-Lite (RTTL)

**Windows 11 本地实时字幕翻译工具 | On-device live caption translation for Windows 11**

[简体中文](#简体中文) · [English](#english)

> **无需云端翻译服务 · 无需下载第三方 AI 模型 · 下载运行目录即可启动**  
> **No cloud translation service · No third-party AI models · Run directly from the distribution folder**

RTTL 将 Windows 11「实时字幕」识别到的文字交给 Microsoft Edge 的**设备端翻译模型**处理，再以独立悬浮窗显示译文。项目本身不提供语音识别模型，也不使用云端翻译回退。首次使用时，可能需要联网下载 **Windows 语音识别资源**和 **Edge 本地翻译模型**；资源准备完成后，字幕翻译功能可在不依赖云端翻译服务的情况下运行。

RTTL reads text from Windows 11 Live Captions, translates it with Microsoft Edge's **on-device translation model**, and displays the result in a separate overlay. The project includes no speech-recognition model and has no cloud translation fallback. An internet connection may be required **initially** to download Windows speech resources and Edge translation models. Once those resources are installed, caption translation does not require a cloud translation service.

---

## 简体中文

### 下载与运行（先看这里）

**普通用户只需要 `RTTL` 文件夹，不需要下载或配置整个开发工程。**

| 方式 | 操作 | 适合谁 |
| --- | --- | --- |
| **方式 A：下载 Release（推荐）** | 前往 [Releases](https://github.com/abian51/Real-Time-Txt-Lite/releases)，下载发布的 Windows 软件压缩包，解压后双击其中的 `start.bat`。 | 普通用户 |
| **方式 B：使用仓库内的 `RTTL` 文件夹** | 在 [项目仓库](https://github.com/abian51/Real-Time-Txt-Lite) 下载完整源码 ZIP，解压后**只保留 `RTTL/` 文件夹**，双击 **`RTTL/start.bat`**。 | 未发布 Release 时，或想使用仓库中的运行文件 |
| **方式 C：使用完整开发仓库** | 下载完整仓库后也可以双击**根目录的 `start.bat`**。其他源码、文档、构建和测试文件主要用于项目开发。 | 开发者 |

> GitHub 网页通常不能直接将仓库内的单个文件夹下载为 ZIP，所以方式 B 需要先下载仓库 ZIP，再提取 `RTTL/`。**请保留 `RTTL` 文件夹内部的完整目录结构，不要只复制 `start.bat`。** 如果 Releases 页面暂无附件，请使用方式 B。

**文件夹分工：**

```text
Real-Time-Txt-Lite/          # 完整项目：开发、构建、测试与记录
├── RTTL/                    # 面向用户的独立运行目录（只需此文件夹）
│   ├── start.bat            # 用户启动入口
│   └── ...                  # 运行所需的其他文件与子目录
├── start.bat                # 完整开发工程的启动入口，也可运行
└── ...                      # 源代码、构建脚本、文档、测试等
```

### 运行环境

- **Windows 11 x64**（暂不支持 Windows 10、macOS 或 Linux）。
- **Windows PowerShell 5.1**、**.NET Framework 4.8.1 Runtime**。
- **Microsoft Edge 148 或更高版本**，以及对应的 Edge 设备端翻译模型。
- 使用实时语音字幕时，需要 Windows「实时字幕」支持的**对应识别语言资源**；**纯文本翻译不需要语音包**。
- 正常使用不需要管理员权限、Visual Studio、.NET SDK、Node.js 或自行编译。企业设备还须允许脚本、DLL、本机回环连接与 Edge Translator API。

**重要：** Windows 语音识别资源与 Edge 翻译模型是两个不同的组件，均不包含在本项目文件夹中。首次缺少资源时需要联网获取；已安装则无需重复下载。无需安装完整的 Windows 显示语言包，也不需要为**翻译目标语言**安装语音包。

### 首次使用

1. 按上文下载并解压。直接运行 **`RTTL/start.bat`**（如果下载 Release，则运行解压包内的 `start.bat`）。
2. 首次打开 Windows **实时字幕（Live Captions）** 时，按照系统提示完成授权与所需识别语言资源的安装。默认翻译方向为**英语（美国）→ 简体中文**，请先将 Windows 实时字幕的「字幕语言」设为 **English (United States)**。
3. 程序打开的本机 Edge 页面会检查翻译模型。如果缺少模型，按照页面提示**手动点击下载微软翻译模型**；不会自动代替你接受微软的下载授权。已安装时可使用页面上的检查功能验证模型连通性。
4. 播放电脑音频后，RTTL 通过 Windows 实时字幕读取识别结果并显示翻译悬浮窗。需要识别麦克风时，可点击悬浮窗右上角的麦克风按钮（须已连接到 Windows 原版字幕窗口）。
5. 使用主窗口的**退出**按钮完整结束。若本次生成了字幕记录，程序会询问保留或删除**本次** TXT；以前的记录不会受到影响。

### 主要功能

- **本地处理：** Windows 实时字幕 → UI Automation 读取文本 → 本机回环 SSE → Edge 设备端 Translator API → WPF 译文悬浮窗。不接管系统音频、不修改 Windows 的语音识别结果，不提供云端翻译回退。
- **字幕翻译：** 默认英语→简体中文；悬浮窗显示当前字幕范围内最近最多三段译文，支持自动换行、滚动、跟随原版字幕位置，也支持取消跟随后独立拖动和调整尺寸。
- **语言设置：** 悬浮窗与网页手动文本翻译各有独立的源语言和目标语言。语言菜单提供微软官方示例中的 **154 个候选语言／变体**；**这不代表所有语言对均可翻译，也不代表 Windows 实时字幕支持识别这些语言**。实际可用性以 Edge `Translator.availability()` 检查为准。
- **方向切换：** 悬浮窗中选择语言后立即应用并保存；选择与另一端相同的语言时会交换方向。切换字幕**源语言**后，仍需手动将 Windows 实时字幕识别语言设置成一致。专用**中英快捷切换按钮**可尝试同步切换 `en-US` / `zh-CN` 的识别语言和翻译方向，只有操作成功后才更新方向。
- **网页文本翻译：** 可手动输入文本，不依赖语音包；网页翻译方向与悬浮窗字幕方向互不干扰。缺少所选模型时由用户主动下载。
- **外观设置：** 翻译字体与颜色、背景透明度（默认 50%）、悬浮窗尺寸和跟随行为等可自定义并保存。窗口默认高度为 160，最小尺寸为 420×160。
- **文字记录：** 字幕和手动翻译默认自动写入本地 UTF-8 TXT，保存原文、译文、时间与识别修订信息；正常退出时可以决定是否保留本次记录。

### 隐私与联网说明

**RTTL 不包含上传字幕、译文或会议 TXT 到项目服务器的功能，也不接入第三方云端翻译接口。** 翻译调用的是 Edge 设备端模型，程序内部组件通过本机回环地址通信。

但是请注意：

- **首次资源安装可能需要联网**：Windows 识别语言包及 Edge 设备端翻译模型由 Microsoft 分发；模型可用性还取决于 Edge 版本、语言对与设备环境。
- **本地记录默认开启**：记录保存在 `%LOCALAPPDATA%\LCET\transcripts`，其中可能包含会议原文和译文的明文信息；程序**没有隐私模式开关**。请根据保密要求妥善管理，使用完毕可在正常退出时删除本次记录。
- **资源占用不为零**：字幕识别、翻译模型和浏览器可能使用 CPU、GPU 与内存。项目自身不捆绑第三方 AI 模型，但不保证在所有设备上完全没有性能影响。
- 本项目不能保证 Windows 或 Edge 自身完全不进行联网通信；**“本地翻译”仅说明 RTTL 不以云端翻译服务处理字幕内容**。

### 开发与项目结构

**完整仓库是开发过程和工程结构的记录，不是普通用户运行 RTTL 的必需下载内容。** 其中的源码、构建脚本、测试脚本和文档用于维护与迭代。普通用户优先使用 Release 或 `RTTL/`。

项目使用 .NET Framework 4.8.1 与 WPF；开发和重新构建需要 Visual Studio Build Tools 的 .NET 桌面构建组件及 .NET Framework 4.8.1 开发工具。用户端不需要这些工具。

如需了解更完整的功能细节和使用限制，请参阅 [RTTL/README.md](RTTL/README.md)；开发者可查看仓库中的部署、依赖及测试文档。

### 版权与第三方权利

Copyright (c) 2026 Real-Time-Txt-Lite (RTTL) 项目作者。保留所有权利。以下授权仅适用于作者有权许可的原创代码及文档。

**允许个人非商业学习、研究和使用。** 复制或修改时须保留版权声明及使用限制。**未经著作权人事先书面许可，禁止将本项目及其修改版、衍生版用于商业用途**，包括但不限于销售、有偿分发、收费服务、付费功能和商业产品集成；改名、重新打包或仅复用部分代码不免除限制。其他未明确授予的权利均予保留。

**本项目不采用 OSI 认可的开源许可证**；仓库公开不等于获得商业使用许可。

RTTL 不是 Microsoft 官方产品，也不代表获得 Microsoft 的认可、合作或背书。Microsoft、Windows、Edge、相关语音资源及翻译模型的权利属于各自权利人；使用这些组件应遵守适用条款。本项目的授权不覆盖第三方组件、模型及用户内容。用户应确保有权处理、记录与分享相关内容。权利人可通过仓库 [Issues](https://github.com/abian51/Real-Time-Txt-Lite/issues) 反馈具体问题与权利证明，请勿公开敏感材料。维护者将在核实后采取适当措施；本说明不保证绝无侵权，也不免除法定责任。

### 当前状态

RTTL 仍属于持续开发和验证中的项目。离线重启、长时间运行、双向实际音频识别与翻译，以及不同设备的性能表现，仍需按测试计划进行实机验证；Edge 模型显示“已安装”不等于整条音频字幕链路已经通过测试。

---

## English

### Download and run (start here)

**End users only need the `RTTL` folder. The full development repository is not required to run the application.**

| Option | Instructions | Intended for |
| --- | --- | --- |
| **A. Download a Release (recommended)** | Open [Releases](https://github.com/abian51/Real-Time-Txt-Lite/releases), download the Windows distribution archive, extract it, and double-click the included `start.bat`. | End users |
| **B. Use the repository's `RTTL` folder** | Download the repository as a ZIP from the [repository home page](https://github.com/abian51/Real-Time-Txt-Lite), extract it, **keep only the `RTTL/` folder**, and run **`RTTL/start.bat`**. | When no Release is available, or to use the repository's distribution files |
| **C. Use the full repository** | The **root-level `start.bat`** can also launch the application. Other repository content primarily documents development, building, and testing. | Developers |

> GitHub does not normally offer a built-in ZIP download for just one repository subfolder. For option B, download the repository ZIP first, then extract `RTTL/`. **Keep all files and subfolders inside `RTTL/` together; `start.bat` alone is not enough.** If Releases has no downloadable asset yet, use option B.

**Directory roles:**

```text
Real-Time-Txt-Lite/          # Complete project: development, builds, tests, history
├── RTTL/                    # Self-contained distribution folder for end users
│   ├── start.bat            # End-user launcher
│   └── ...                  # Other required runtime files and subfolders
├── start.bat                # Full-project launcher (also works)
└── ...                      # Source, build scripts, documentation, tests, etc.
```

### System requirements

- **Windows 11 x64** (Windows 10, macOS, and Linux are not currently supported).
- **Windows PowerShell 5.1** and the **.NET Framework 4.8.1 Runtime**.
- **Microsoft Edge 148 or later**, with the required Edge on-device translation model(s).
- A supported Windows Live Captions **recognition-language resource** for speech captions; **manual text translation does not require a speech pack**.
- No administrator access, Visual Studio, .NET SDK, Node.js, or local compilation is normally required. Managed devices must allow scripts, DLLs, local loopback connections, and the Edge Translator API.

**Important:** Windows speech recognition resources and Edge translation models are **separate components** and are not bundled in this repository's runtime folder. Missing resources may require an internet connection for their initial download. You do not need a full Windows display language pack or a speech pack for the **target translation language**.

### Quick start

1. Download and extract the distribution, then run **`RTTL/start.bat`** (or the `start.bat` included in the Release archive).
2. On first use, complete the required Windows **Live Captions** setup and install the recognition language resource. The default translation direction is **English (United States) → Simplified Chinese**. Select **English (United States)** as the Windows Live Captions recognition language.
3. The local Edge page checks for translation models. If a model is missing, **manually click the Microsoft translation model download button**. The software does not automatically accept Microsoft's download consent on your behalf. Once models are installed, you can use the page to check translation connectivity.
4. Play system audio to receive translated captions in the overlay. For microphone recognition, use the overlay's microphone button; this requires a connection to the original Windows Live Captions window.
5. Use **Exit** in the main window to shut down cleanly. If the session contains saved text, the app will ask whether to keep or delete **that session's** TXT file; older sessions remain untouched.

### Features

- **On-device pipeline:** Windows Live Captions → UI Automation text capture → local loopback SSE → Edge on-device Translator API → WPF translated-caption overlay. RTTL does not take over audio capture, modify recognition output, or fall back to a cloud translation service.
- **Caption overlay:** Defaults to English → Simplified Chinese. Displays up to the three most recent segments within the current Windows caption range, with wrapping, scrolling, optional follow mode, and independent dragging/resizing when follow mode is off.
- **Language selection:** Both the overlay settings and the manual text translation page have separate source/target selectors with **154 candidate languages/variants** from a Microsoft sample. **This is not a guarantee that every language pair is supported or that Windows Live Captions can recognize all listed languages.** Translation support is checked using Edge `Translator.availability()`.
- **Direction switching:** Overlay language changes apply and save immediately. Selecting the opposite side's language swaps the pair. After changing a caption **source language**, you must still set the Windows Live Captions recognition language accordingly. The dedicated **English/Chinese shortcut** attempts to switch both recognition (`en-US` / `zh-CN`) and translation direction, and applies the change only after successful confirmation.
- **Manual text translation:** Enter text in the local Edge page without a Windows speech pack. Its source/target direction is independent of the caption overlay; missing models require a user-initiated download.
- **Display preferences:** Configurable font, text color, background opacity (50% by default), overlay size and follow behavior. The default height is 160; the minimum window size is 420×160.
- **Session transcripts:** Caption and manual translations are written automatically to local UTF-8 TXT files, including source text, translation, timestamps, and recognition revisions. On normal exit, you can choose whether to retain the current session's file.

### Privacy and connectivity

**RTTL does not implement a feature that uploads captions, translations, or session TXT files to a project-operated server, nor does it use a third-party cloud translation API.** Translation uses Edge's on-device model, with local loopback communication between RTTL components.

Please note:

- **First-time resource downloads may need internet access.** Windows recognition resources and Edge on-device models are distributed by Microsoft. Model availability also depends on the Edge version, language pair, and device environment.
- **Local transcript recording is enabled by default.** Plaintext transcripts are stored in `%LOCALAPPDATA%\LCET\transcripts` and may contain confidential meeting content. There is **no privacy-mode toggle**; manage these files according to your data-handling requirements. You can delete the current session's record on normal exit.
- **Resource usage is not zero.** Windows speech recognition, Edge's translation models, and browser processes can consume CPU, GPU, and memory. RTTL bundles no third-party AI model, but cannot guarantee zero performance impact.
- RTTL cannot guarantee that Windows or Edge itself never performs network activity. **“On-device translation” means RTTL does not depend on cloud services to translate caption content.**

### Development and repository structure

**The full repository documents the development process and project layout; end users do not need it to run RTTL.** It includes source files, build scripts, tests, and development documentation. End users should use a Release or the `RTTL/` folder.

The application uses .NET Framework 4.8.1 and WPF. Building the project requires Visual Studio Build Tools with .NET desktop build components and .NET Framework 4.8.1 development tools. End users do not need these.

For more detailed behavior, constraints, and license information, see [RTTL/README.md](RTTL/README.md). Developers can also consult the deployment, dependency, and test documents in the repository.

### Copyright, license, and third-party rights

Copyright (c) 2026 the Real-Time-Txt-Lite (RTTL) author(s). All rights reserved. The following permission covers only original project code and documentation that the author(s) have the right to license.

**Personal, non-commercial learning, research, and use are permitted.** Copies and modifications must retain the copyright notice and use restrictions. **Commercial use of this project, modified versions, or derivative works is prohibited without prior written permission from the copyright holder**, including sale, paid distribution, paid services, paid features, and integration into commercial products. Renaming, repackaging, or reusing only portions of the code does not remove these restrictions. All other rights are reserved.

**This is not an OSI-approved open-source license.** Public repository access does not grant commercial-use rights.

RTTL is an independent project, not an official Microsoft product, and is not affiliated with, endorsed by, or certified by Microsoft. Rights to Microsoft, Windows, Edge, related speech resources, translation models, and their marks belong to their respective owners; their use remains subject to applicable third-party terms. This project's permission does not cover third-party software, models, or user content. Users are responsible for having the necessary rights to process, record, or share content. Rights holders may report specific concerns with supporting information via [Issues](https://github.com/abian51/Real-Time-Txt-Lite/issues); do not post sensitive material publicly. Confirmed issues will be addressed as appropriate. This notice does not guarantee the absence of infringement or waive legal obligations.

### Project status

RTTL remains under active development and validation. Offline restarts, sustained background operation, real-world two-way audio recognition/translation, and performance across target devices require further testing. An “installed” model status alone does **not** confirm that the full audio-to-caption pipeline has passed validation.
