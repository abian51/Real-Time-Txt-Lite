# real-time-txt-lite (RTTL)

Windows 实时字幕本地翻译工具 / Local translation overlay for Windows Live Captions.

[中文](#中文) | [English](#english)

**当前仅支持 Windows 11 x64，不支持 Windows 10、macOS 或 Linux。** 实时字幕只需在其“字幕语言”菜单中为所用识别语言下载语音包，无需安装完整的 Windows 显示语言包，也不必为翻译目标安装语音包。翻译需要对应的 Edge 本地翻译模型；缺少资源时需联网下载。这些资源不包含在本项目文件夹中，已安装的无需重复下载。纯文本翻译不要求 Windows 语音包。

**Currently supports Windows 11 x64 only; Windows 10, macOS, and Linux are not supported.** Download the speech pack for the recognition language from the Caption language menu in Windows Live Captions. A complete Windows display language pack is not required, nor is a speech pack for the translation target. Translation uses separate Edge on-device models. Missing resources require internet access and are not included in this project folder; installed resources do not need to be downloaded again. Manual text translation does not require Windows speech packs.

## 中文

### 项目介绍

real-time-txt-lite（简称 RTTL）是 Windows 11 上的本地翻译工具，提供会议、课程和系统音频的多语言实时字幕翻译，以及多语言手动文本翻译。本项目不是 Microsoft 官方产品；Windows 实时字幕和 Edge Translator API 由 Microsoft 提供。

**不会自动识别正在说哪种语言，也不会根据语音自动切换识别语言或翻译方向。** 使用前需指定源语言，并让 Windows 实时字幕的识别语言与其一致。只有外面的中英快捷按钮可以在用户点击后尝试同步切换 `en-US` / `zh-CN`；多语言选择器及交换箭头不会替你切换 Windows 识别语言。

**悬浮窗设置和网页均提供 154 个候选语言／变体，源语言与目标语言独立选择，默认英文→中文。** 英文、简体中文、日文置顶，其余按官方示例顺序排列。外面的中英快捷按钮保留英语（美国，`en-US`）与中文（简体，中国大陆，`zh-CN`）的同步识别切换。

154 项取自[微软官方示例](https://microsoftedge.github.io/Demos/built-in-ai/playgrounds/translator-api/)，是候选项数量，不代表全部语言对已经可用或通过验证，也不代表 Windows 实时字幕能识别所有候选语言。实际翻译能力按 Edge 设备内 Translator API 检查。其他源语言需在 Windows 实时字幕中手动选择并下载所需语音包；网页手动翻译方向不会更改悬浮字幕方向。

处理流程：Windows 实时字幕识别语音 → UI Automation 读取字幕 → Microsoft Edge 本地翻译模型 → 悬浮窗显示译文。

- 支持英语译中文、中文译英语，默认英语译中文。
- 设置中选中源语言或目标语言即应用并保存，无需点击应用或勾选确认；若选中另一端的语言则自动交换。Windows 识别语言仍需手动匹配。
- 悬浮窗可跟随 Windows 字幕，显示最多三段译文；等待新结果或可见范围短暂为空时保留上一屏，当前译文返回后替换。长译文自动换行，中英文逗号可分段，数字中的逗号保留。
- 译文默认黑色，设置中可用色块切换黑、白、绿、蓝、红；颜色立即生效并保存。默认窗口高度由 240 减为 160，其他自定义高度保留。
- 提供 Windows 字幕麦克风识别开关，以及仅限中英的识别语言／翻译方向同步切换按钮；均依赖 Windows 字幕界面控制成功。
- Edge 页面支持多语言手动输入文本翻译；中英方向复用字幕模型，其他方向按需准备，切换时释放旧手动模型。
- 字幕与手动翻译自动写入本次会议的 UTF-8 TXT，正常退出时可选择保留或删除本次文件。
- 使用设备内翻译，不提供云端翻译回退；程序不接管音频或修改 Windows 识别结果。
- 以文件夹形式分发，提供 `RTTL.exe` 和原 BAT 启动方式，无需安装开发工具或在使用者电脑上编译。EXE 不是单文件版，必须保留随包网页和配置。

### 版权与使用限制

Copyright (c) 2026 real-time-txt-lite（RTTL）项目作者。保留所有权利。本声明仅适用于作者有权许可的本项目原创代码和文档。

允许个人非商业学习、研究和使用；复制或修改时须保留本版权声明、使用限制和第三方权利说明。**未经著作权人事先书面许可，禁止将本项目或其修改版、衍生版用于商业用途**，包括出售、有偿分发、收费服务、付费功能及商业产品集成。修改、改名、重新打包或仅使用部分代码，不免除这些限制。其他未明确授予的权利均予保留。

本项目采用非商业使用限制，不属于 OSI 定义的开源许可；在 GitHub 上公开可见不等于获得商用授权。需要商业授权或其他许可时，请事先联系著作权人并取得书面许可。

### 第三方权利与侵权反馈

本项目为独立开发的非 Microsoft 官方工具，不代表与 Microsoft 存在隶属、合作、认证或背书关系。Microsoft、Windows、Edge 及相关名称、商标、语音资源和翻译模型的权利归各自权利人所有，仅为说明兼容性与运行依赖而提及；使用这些组件仍须遵守其各自条款。本项目的许可不覆盖第三方组件、模型或用户输入的内容，也不授予其相关权利。

使用者应确保对输入、翻译、记录及分享的文字或会议内容拥有必要权利或授权，不得利用本项目侵犯版权、隐私或其他合法权益。若权利人认为仓库内容侵权，请通过仓库 Issue 或维护者公开联系方式提供涉及内容、权利证明及具体请求；请勿公开敏感材料。维护者将核实，并在确认问题后采取删除、更正或其他适当处理。本声明不构成对绝不侵权的保证，也不免除依法应承担的责任。

### 数据与隐私

**内容数据纯本地处理，本项目不会上传。** 字幕原文、译文、手动输入和会议记录在本机处理与保存，不由本项目上传至外部服务器，也不使用云端翻译。程序与 Edge 翻译页面仅通过 `127.0.0.1` 本机回环地址交换数据。

语言包、翻译模型的首次下载需要联网，但不等于上传会议内容。此声明针对本项目的数据处理流程，不是对 Windows、Edge 或整台电脑完全不联网的保证，也不涵盖用户自行同步或分享文件。会议 TXT 为本地明文文件，仍需按组织保密要求管理。

### 运行环境

| 项目 | 要求 |
| --- | --- |
| 操作系统 | Windows 11，x64，当前登录的交互式桌面会话 |
| 脚本运行时 | EXE 启动不依赖 PowerShell；仅 BAT 启动需要 64 位 Windows PowerShell 5.1 |
| .NET 运行时 | .NET Framework 4.8.1 Runtime，不需要 .NET SDK |
| 浏览器 | Microsoft Edge 148 或更新版本，允许使用设备内 Translator API |
| 语音识别 | Windows 实时字幕支持的源语言及其语音包；默认使用 English (United States)，中英快捷切换另需中文（简体，中国大陆） |
| 翻译模型 | Edge 管理的所选语言对本地模型；默认检查英译中、中译英，其他方向按需准备，缺少时需由用户点击下载 |
| 组织策略 | 允许启动脚本、加载 DLL、访问 UI Automation 和本机回环通信 |

程序按普通用户权限运行，无需以管理员身份启动。首次准备 Windows 语言资源和 Edge 翻译模型可能需要联网；这些资源不包含在 RTTL 文件夹内。模型就绪不等于已完成离线或性能验收，仍需在目标电脑验证。Edge 模型会占用系统资源。

### 启动方式

1. 将整个 RTTL 文件夹复制到电脑，保持内部目录和文件的相对位置，不要只复制启动文件。
2. 解压后双击 [RTTL.exe](RTTL.exe)，不要直接在 ZIP 内运行，也不要只复制 EXE。仍可使用 [start.bat](start.bat)，或在此文件夹的终端中运行：

   ```powershell
   .\start.bat
   ```

3. 首次使用时，完成 Windows 实时字幕授权并安装识别语言包。默认英译中，请先在原版字幕中选择 English (United States)，并确认能看到英文原文；程序启动时不自动检测或更改识别语言。
4. Edge 状态页会自动打开并检查翻译模型。显示“点击下载微软翻译模型”时点击下载；显示“下载中...”时等待，双向模型就绪后显示“已安装”。不要关闭使用中的翻译页面。
5. 播放系统音频即可开始翻译。需要识别麦克风时，可使用悬浮窗的麦克风按钮；它控制的是 Windows 字幕的麦克风识别，不是系统静音。方向按钮仅在 en-US 与 zh-CN 之间同步切换，所需语言包须提前安装；切换失败时按提示在原版字幕中检查。
6. 使用悬浮窗右上角的退出按钮结束。有本次记录时，选择“是”保留并打开 TXT，选择“否”仅删除本次文件，不影响以前的记录。

网页文本翻译可分别选择源语言和目标语言；缺少所选模型时点击“下载模型并翻译”，不会因选择语言而自动下载。Windows 语音包自动检测已取消，不再显示“可用”或“未安装”。标题旁保留“字幕语音包安装说明”链接；请在 Windows 实时字幕的“字幕语言”菜单中选择所用识别语言并下载，而不是安装完整显示语言包。Edge 模型单独管理，纯文本翻译不需要 Windows 语音包。

悬浮窗设置也可分别选择源语言、目标语言，选中即应用并保存，不再提供“应用方向”或识别确认勾选。选中另一端当前使用的语言时自动交换，避免相同语言对。更换源语言后，请在 Windows 实时字幕中手动选择对应识别语言；提醒不阻止翻译，也不代表已自动检测匹配。字幕模型缺少时，到 Edge 页面点击“下载所选字幕模型”。中英字幕复用原模型，其他方向最多保留一个额外字幕模型，切换后释放。发布目录内的共用语言目录必须随整个文件夹保留。

网页首次打开时使用悬浮窗当前翻译方向，之后两端独立，重连不覆盖网页选择。两端语言选择器旁均有交换方向箭头，点击交换各自源／目标语言；悬浮窗交换会立即应用并保存，源语言改变后仍须手动匹配 Windows 识别语言，但无需确认勾选。外面的中英快捷切换按钮不变。

悬浮窗采用紧凑布局，默认高度 124，底部状态字号增至 9。右下角手柄可调整窗口尺寸；跟随 Windows 字幕时仅调整高度。设置中的“窗口高度”滑杆支持 96–1200，立即生效并保存；跟随时实际高度仍受屏幕可用空间限制。“译文行数”默认保留四条，可选 2–6 条，选中立即保存，长译文仍自然换行；已有个人选择不强制覆盖。设置窗口默认加宽至最多 900、高度增至 340，行数标签与选择框保持同一行。旧默认高度会迁移，自定义高度保留。字幕采集优先使用控件的 UIA 可见文字范围；控件不支持时回退原读取方式，具体 Windows 版本仍需实测。

等待新译文时保留最近译文，不因字幕滚动或短暂空快照清空；实时结果返回时，即使源句刚滚出画面，也可更新最近可用译文。较旧结果不能覆盖较新译文，历史补译不回填。当前未完句只剩可见尾部时仍允许其译文显示。点击“清空”、切换方向或会话重置仍清除旧显示；首次还没有译文时仍会等待。

翻译最多同时执行两条，两个通用槽位共用队列，优先处理最新字幕；任务优先按 A、B、A、B 交替分派，轮到的槽忙时使用另一空闲槽，不固定区分实时和历史用途。同一句段的两个版本不会同时执行，新版需等旧调用结束；识别修订仍可后续重译。手动翻译也计入两条总上限。第二槽按需创建独立 Translator 会话，切换会话或退出时释放；这不等于控制模型线程数，也不保证提速或修复设备性能问题。默认静默去抖 700 ms，同一片段的连续预览间隔 1500 ms；已有个人设置保留。

会议文件位于 `%LOCALAPPDATA%\LCET\transcripts`，后台每 4 秒合并追加一次 TXT，正常退出立即补写剩余记录，再询问是否保留。手动翻译只有实际写入后才确认“已记录”。强制结束或断电可能丢失尚未写入的内容，也无法保证弹出保留提示。文件可能包含原文、译文和手动输入的敏感信息，请按组织保密要求管理。若启动失败，请查看控制台；如被组织策略阻止，请联系 IT，不要关闭安全防护或绕过策略。

### 数据位置与故障排查

项目对外名称为 real-time-txt-lite（RTTL），但为兼容已有设置，内部 DLL、命名空间和用户数据目录仍沿用 `LCET`，不应手动改名。

| 内容 | 位置 |
| --- | --- |
| 个人设置 | `%LOCALAPPDATA%\LCET\settings.json` |
| 本地会议记录 | `%LOCALAPPDATA%\LCET\transcripts` |
| 技术日志 | `%LOCALAPPDATA%\LCET\logs\technical.log` |

| 现象 | 建议检查 |
| --- | --- |
| 原版 Windows 字幕也没有原文 | 先检查音频、字幕识别语言及语音包；RTTL 无法修复 Windows 识别结果 |
| 有原文但没有译文 | 检查 Edge 页面是否仍打开、所选字幕模型是否就绪，以及 Windows 识别语言是否与所选源语言匹配 |
| 中英快捷切换失败 | 在 Windows 字幕菜单中手动选择语言，再检查 RTTL 设置；其他语言本来就需要手动设置 |
| 模型不可用或无法下载 | 以页面实际状态为准，检查 Edge 版本、网络及组织策略；不要关闭安全防护或使用云端替代 |
| 更新后仍是旧界面或旧行为 | 正常退出旧实例后重新运行，不要只刷新网页；先处理本次记录保留提示 |

报告问题时请提供 Windows/Edge 版本、所选语言对、复现步骤及去敏后的状态或日志片段。不要公开提交会议 TXT、个人设置、完整浏览器资料或含敏感内容的截图。更多中文操作说明见 [README.txt](README.txt)。

### GitHub 发布说明

本目录是便携运行包，保留完整目录后运行 [RTTL.exe](RTTL.exe)，也可使用原 [start.bat](start.bat)。EXE 为未签名的 x64 .NET Framework 4.8.1 图形程序，不需要管理员权限或 PowerShell 宿主；系统或组织策略仍可能阻止运行，应联系 IT 审批，不绕过安全防护。EXE 本次仅完成编译和静态检查，未执行启动验收。模型与语音包不随包分发，复制此目录不会复制模型或用户数据。运行时会在上述用户目录写入设置和记录，因此不是“所有数据都留在便携文件夹内”的完全便携模式。

如果只上传本目录，得到的是含预编译 DLL 的运行包，不是可独立重新构建的源码仓库；仅凭本目录不能完整重建 DLL。使用、修改和再分发须遵守上文“版权与使用限制”，并遵守适用的第三方条款。能从 GitHub 下载不等于获得商用授权，本说明不授予 Microsoft 组件或模型的相关权利。

### 已知限制与待验证项

- **不支持语言自动检测或跟随说话者自动切换。** 多语言混说可能识别错误；语言选择立即生效并不代表实际 Windows 识别语言已匹配，程序只作提醒。
- **154 个候选项不等于 154 种可用识别语言或任意可用翻译组合。** 能否识别由 Windows 决定，能否翻译由当前 Edge、模型和策略决定；没有逐一验证所有语言对。
- **不是完整、无遗漏的会议转写或录音工具。** 只读取 Windows 当前可访问的字幕，默认显示四条，可选 2–6 条；识别修订、字幕滚动、延迟、队列过载、翻译或磁盘错误都可能导致记录缺句，不能保证逐字准确。
- **依赖 Windows UI Automation 和 Edge。** 系统更新、字幕界面变化或企业策略可能影响捕获、跟随、麦克风及中英快捷切换。关闭翻译页面会中断翻译；没有 OCR 或云端回退。
- **“lite”不代表零资源占用。** Edge 本地模型消耗内存和计算资源；已有用户反馈并行运行时识别漏句增加，原因尚未确认，需要在目标电脑与仅开 Windows 字幕进行对照，不能保证不影响识别或实时性能。
- **离线与兼容性仍需实机验证。** 首次资源准备需要联网；已安装模型不代表任意电脑重启后都能离线使用。麦克风开启、完整中译英识别切换、混合 DPI、多屏及长时间运行仍有验证缺口。
- **当前界面主要为中文，英文说明不代表英文界面。** 部分测试使用模拟 Translator 和未显示的 WPF 窗口，不等于真实 Edge 下载、真实语音或全桌面验收；本机 HTTP 拒绝请求的回归测试仍有连接重置失败，不能宣称全套测试通过。
- **自动记录不能关闭，记录是明文。** 正常退出可删除本次文件，强制结束不能保证提示；历史文件不会自动清理。请勿用于未经授权记录的会议，也不要将译文作为法律、医疗或其他高风险判断的唯一依据。

## English

### Project Overview

real-time-txt-lite (RTTL) is a local translation tool for Windows 11, offering multilingual live caption translation for meetings, classes, and system audio, plus multilingual manual text translation. This is not an official Microsoft product; Windows Live Captions and the Edge Translator API are provided by Microsoft.

**RTTL does not automatically detect the spoken language or switch recognition languages or translation directions based on speech.** Select the source language and match it in Windows Live Captions. Only the external English/Chinese shortcut attempts a synchronized `en-US` / `zh-CN` switch after a user click; multilingual selectors and swap arrows do not change Windows recognition settings.

**Overlay settings and the webpage offer 154 candidate languages/variants with separate source and target selectors, defaulting to English-to-Chinese.** English, Simplified Chinese, and Japanese appear first; the remaining entries retain the official demo order. The external bilingual shortcut still synchronizes recognition between English (United States, `en-US`) and Chinese (Simplified, Mainland China, `zh-CN`).

The 154 entries come from [Microsoft's official demo](https://microsoftedge.github.io/Demos/built-in-ai/playgrounds/translator-api/). This count does not mean every pair is available or validated, or that Windows Live Captions can recognize every candidate language. Actual translation support is checked through Edge's on-device Translator API. For other source languages, manually choose the recognition language and download its speech pack in Windows Live Captions. Web manual translation selections remain independent of overlay caption selections.

Pipeline: Windows Live Captions recognizes speech → UI Automation reads captions → Microsoft Edge translates with on-device models → a floating overlay displays translations.

- Supports English-to-Chinese and Chinese-to-English translation; English-to-Chinese is the default.
- Source and target selections in overlay Settings apply and save immediately, without an Apply button or confirmation checkbox. Selecting the language currently used on the other side swaps the pair. Match the Windows recognition language manually.
- The overlay can follow Windows captions and shows up to three translation segments. It retains the previous frame while new results are pending or visible text is briefly empty, then replaces it when current translations arrive. Long text wraps automatically; English and Chinese commas can split segments, while commas within numbers are preserved.
- Translation text defaults to black. Settings offer black, white, green, blue, and red swatches; changes apply immediately and are saved. Default window height is reduced from 240 to 160, preserving other custom heights.
- Includes controls for Windows caption microphone recognition and English/Chinese-only synchronized recognition-language/translation-direction switching. Both depend on successful interaction with the Windows captions interface.
- The Edge page supports multilingual manual text translation. English/Chinese directions reuse caption models; other pairs are prepared on demand, and the previous manual model is released when selections change.
- Captions and manual translations are automatically saved to a UTF-8 TXT file for the current session. Normal exit lets you keep or delete that session's file.
- Uses on-device translation with no cloud translation fallback. The application does not take over audio capture or modify Windows recognition results.
- Distributed as a portable folder with `RTTL.exe` and the original BAT launcher. No developer tools or compilation are required on the recipient's computer. The EXE is not a single-file bundle; retain the accompanying web and configuration files.

### Copyright and Use Restrictions

Copyright (c) 2026 the authors of real-time-txt-lite (RTTL). All rights reserved. This notice applies only to original project code and documentation that the authors have the right to license.

Personal non-commercial learning, research, and use are permitted. Copies and modifications must retain this copyright notice, these restrictions, and third-party rights notices. **Commercial use of this project, modified versions, or derivative works is prohibited without prior written permission from the copyright holder**, including sale, paid distribution, paid services or features, and integration into commercial products. Modification, renaming, repackaging, or reuse of only part of the code does not remove these restrictions. All other rights are reserved.

These non-commercial restrictions are not an OSI-defined open-source license. Public availability on GitHub does not grant commercial permission. Obtain written authorization from the copyright holder before any commercial or otherwise unpermitted use.

### Third-Party Rights and Infringement Reports

This is an independently developed, unofficial tool, with no claimed Microsoft affiliation, partnership, certification, or endorsement. Microsoft, Windows, Edge, and associated names, trademarks, speech resources, and translation models belong to their respective rights holders and are mentioned only to describe compatibility and dependencies. Their own terms continue to apply. This project's permissions do not cover third-party components, models, or user content and grant no rights to them.

Users must have the necessary rights or authorization to input, translate, record, and share text or meeting content, and must not infringe copyright, privacy, or other rights. Rights holders who believe repository content is infringing may contact the maintainer through a repository Issue or published contact channel, identifying the content, evidence of rights, and the requested action. Do not disclose sensitive materials publicly. The maintainer will review reports and remove, correct, or otherwise address confirmed issues as appropriate. This notice is not a guarantee of non-infringement and does not exclude responsibilities imposed by law.

### Data and Privacy

**Content data is processed entirely locally; this project does not upload it.** Source captions, translations, manual input, and session records are processed and stored on the computer. This project does not upload them to external servers or use cloud translation. The application and the Edge translation page exchange data only through the local loopback address, `127.0.0.1`.

Initial language-pack and translation-model downloads require internet access; this does not mean meeting content is uploaded. This statement covers this project's data-processing workflow, not a guarantee that Windows, Edge, or the entire computer makes no network connections. It also excludes files that users independently sync or share. Session TXT files are stored locally in plain text and must still be handled according to organizational confidentiality rules.

### Runtime Requirements

| Component | Requirement |
| --- | --- |
| Operating system | Windows 11, x64, in the currently signed-in interactive desktop session |
| Script runtime | The EXE does not need PowerShell; only the BAT launcher requires 64-bit Windows PowerShell 5.1 |
| .NET runtime | .NET Framework 4.8.1 Runtime; the .NET SDK is not required |
| Browser | Microsoft Edge 148 or later, with access to the on-device Translator API |
| Speech recognition | A source language supported by Windows Live Captions and its speech pack; English (United States) is the default, with Chinese (Simplified, Mainland China) additionally needed for the bilingual shortcut |
| Translation models | Edge-managed local models for the selected pair; English-to-Chinese and Chinese-to-English are checked by default, with other pairs prepared on demand. Missing models require a user-initiated download |
| Organizational policies | Must allow launcher scripts, DLL loading, UI Automation access, and local loopback communication |

Run as a standard user; administrator privileges are not required to launch the application. Initial Windows language resources and Edge translation models may require internet access and are not included in this folder. Model readiness alone does not certify offline operation or performance; validate these on the target computer. Edge models consume system resources.

### How to Start

1. Copy the entire RTTL folder to the computer, preserving its internal structure. Do not copy only the launcher.
2. Extract the package and double-click [RTTL.exe](RTTL.exe). Do not run it inside the ZIP or copy only the EXE. Alternatively, use [start.bat](start.bat), or run the following command in a terminal opened in this folder:

   ```powershell
   .\start.bat
   ```

3. On first use, complete Windows Live Captions consent and install the recognition language pack. For the default English-to-Chinese direction, select English (United States) in Windows Live Captions and verify that English source captions appear. Startup does not automatically detect or change the recognition language.
4. The Edge status page opens automatically and checks the translation models. If it shows “点击下载微软翻译模型” (download Microsoft translation models), click it. Wait while “下载中...” (downloading) is displayed; “已安装” (installed) means both directions are ready. Keep the translation page open during use.
5. Play system audio to begin. Use the overlay microphone button if microphone recognition is needed; it controls Windows caption recognition, not system mute. The direction button switches recognition and translation between en-US and zh-CN only. Install the required language packs first; if switching fails, follow the warning and check the original Windows caption settings.
6. Exit using the overlay's top-right exit button. If the session has records, choose Yes to keep and open its TXT file, or No to delete only that session's file. Earlier session files are not deleted.

Session files are stored in `%LOCALAPPDATA%\LCET\transcripts`. The background writer appends a combined batch every 4 seconds; normal exit immediately drains remaining entries before asking whether to keep the file. Manual recording is acknowledged only after writing succeeds. Forced termination or power loss can lose unwritten content and cannot guarantee a keep/delete prompt. Files may contain sensitive source text, translations, and manual input; handle them according to your organization's confidentiality rules. If startup fails, check the console. Contact IT for policy restrictions; do not disable security protections or bypass policies.

For manual text, select source and target languages independently. Missing models are prepared only after clicking “下载模型并翻译” (download model and translate). Automatic Windows speech-pack checks and installation status labels have been removed. The heading retains a link to Microsoft's official speech-pack instructions: choose and download the recognition language in the Caption language menu of Windows Live Captions, rather than installing a complete display language pack. Edge models are managed separately; text-only translation does not require Windows speech packs.

Overlay source and target selections apply and save immediately; there is no Apply Direction button or recognition confirmation checkbox. Selecting the language used on the other side swaps the pair. After changing the source, manually select that recognition language in Windows Live Captions. The reminder does not block translation or certify a match. If the selected caption model is missing, click “下载所选字幕模型” (download selected caption model) on the Edge page. English/Chinese models are reused; at most one extra caption model is retained for other pairs and released on changes. Preserve the shared language catalog included in the release folder.

On first opening, the webpage adopts the overlay's current translation direction once. The two directions are independent afterward, including reconnects. Both language selectors have a swap-arrow button that exchanges their own source and target; overlay swaps apply and save immediately. Match a changed caption source in Windows manually; no confirmation checkbox is required. The external English/Chinese shortcut is unchanged.

The compact overlay defaults to height 124, with footer status text at size 9. Use the enlarged bottom-right grip to resize; docking follows Windows caption width and allows height changes only. Settings provide a “窗口高度” (window height) slider from 96 to 1200 and “译文行数” (translation rows), defaulting to four and adjustable from 2 to 6. Changes apply and save immediately; long translations wrap naturally, and existing personal row selections are preserved. The settings window defaults to up to 900 wide and 340 high, and the row label and selector stay together. Old default heights migrate, while custom heights are preserved. Docked height remains limited by available screen space. Capture prefers UIA visible text ranges, with the original reading method as a fallback for unsupported controls. Actual behavior still requires validation on the target Windows version.

While new translations are pending, the previous translations remain visible through scrolling or briefly empty snapshots. A live result may update the latest available translation even if its source has just scrolled away; older results cannot overwrite newer translations, and historical backfill is not replayed. The visible suffix of an in-progress sentence remains eligible for display. Clear, direction changes, and session resets still remove the previous display. Before the first successful translation, the waiting message remains normal.

At most two translations execute concurrently in interchangeable slots sharing one queue, prioritizing the latest captions. Assignment prefers A, B, A, B; if the preferred slot is busy, the other free slot is used. Neither slot is reserved for live or historical work. Revisions of the same segment never execute simultaneously; a newer revision waits for the older call to finish. Recognition revisions may still be translated later. Manual translation shares the same two-call limit. The second slot creates a separate Translator session on demand and releases it on session reset or exit. This does not control inference thread counts or guarantee speedups or a fix for device performance issues. The default quiet debounce is 700 ms and continuous previews of the same segment are spaced by 1500 ms; existing personal settings are preserved.

### Local Data and Troubleshooting

The public project name is real-time-txt-lite (RTTL). Internal DLL names, namespaces, and data paths still use `LCET` for compatibility; do not rename them manually.

| Data | Location |
| --- | --- |
| Settings | `%LOCALAPPDATA%\LCET\settings.json` |
| Session records | `%LOCALAPPDATA%\LCET\transcripts` |
| Technical log | `%LOCALAPPDATA%\LCET\logs\technical.log` |

| Symptom | Check |
| --- | --- |
| No source text in Windows Live Captions | Check audio, recognition language, and its speech pack first; RTTL cannot fix Windows recognition |
| Source captions but no translation | Keep the Edge page open, check the selected caption model, and match the Windows recognition language to the selected source |
| Bilingual shortcut fails | Select the language manually in Windows captions, then check RTTL Settings; other languages always require manual selection |
| Model unavailable or download fails | Check the actual Edge status, browser version, network, and organizational policies; do not disable protections or substitute cloud translation |
| Old behavior after updating | Exit the old instance normally and restart, handling its record-retention prompt first; refreshing the page alone is insufficient |

For bug reports, include Windows/Edge versions, the selected language pair, reproduction steps, and redacted status or log excerpts. Do not publicly submit meeting TXT files, personal settings, full browser profiles, or sensitive screenshots.

### Publishing on GitHub

This directory is a portable runtime package. Preserve its structure and run [RTTL.exe](RTTL.exe), or use the original [start.bat](start.bat). The EXE is an unsigned x64 .NET Framework 4.8.1 GUI application, without a PowerShell host or administrator requirement. System or organizational policies may still block it; contact IT rather than bypassing protections. This EXE was compiled and statically checked only, not launched for runtime acceptance. Speech packs and translation models are not bundled, and copying the folder does not copy models or user data. Settings and records are written to the user directories above, not exclusively inside the portable folder.

Uploading only this directory publishes a runtime package with a precompiled DLL, not independently rebuildable source; this directory alone cannot fully rebuild the DLL. Use, modification, and redistribution are subject to Copyright and Use Restrictions above and applicable third-party terms. Availability on GitHub does not grant commercial permission or rights to Microsoft components or models.

### Known Limitations and Validation Gaps

- **No automatic language detection or speech-driven switching.** Mixed-language speech may be recognized incorrectly. Immediate selection changes do not certify a matching Windows recognition language; RTTL only displays a reminder.
- **154 candidates are not 154 supported recognition languages or universally supported translation pairs.** Recognition depends on Windows; translation depends on Edge, models, and policy. Not all pairs have been validated.
- **Not a complete, lossless transcript or audio recorder.** Only currently accessible Windows captions are read; four translations are displayed by default, adjustable from 2 to 6. Recognition revisions, scrolling, delays, queue overload, translation failures, or disk errors can leave gaps. Verbatim accuracy is not guaranteed.
- **Depends on Windows UI Automation and Edge.** Updates, UI changes, and policies may affect capture, docking, microphone controls, and bilingual switching. Closing the translation page interrupts translation. There is no OCR or cloud fallback.
- **"lite" does not mean zero resource use.** Edge models consume memory and compute resources. A user reported increased recognition gaps during concurrent use; the cause remains unconfirmed and requires comparison against Windows captions alone on the affected computer. No guarantee of unaffected recognition or real-time performance is made.
- **Offline operation and compatibility require device testing.** Initial resource setup needs internet access; installed models do not certify offline use after restart on every computer. Microphone activation, full Chinese recognition switching, mixed-DPI/multi-monitor use, and long sessions still have validation gaps.
- **The UI is primarily Chinese.** English documentation does not imply an English UI. Some tests use a mocked Translator and unshown WPF windows rather than real Edge downloads, speech, or desktop acceptance. Rejected-request loopback HTTP regression tests still have connection-reset failures; the complete suite must not be described as passing.
- **Automatic recording cannot be disabled, and records are plain text.** Normal exit can delete the current file, but forced termination cannot guarantee a prompt; older files are not cleaned up automatically. Obtain recording authorization and do not rely solely on translations for legal, medical, or other high-stakes decisions.