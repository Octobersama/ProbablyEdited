# ProbablyEdited / ProbablyFonts 严格可维护性审查

审查日期：2026-11-13

## 后续处置（2026-11-13）

以下审查正文及源码链接保留修复前的 `main` 基线。后续按用户确认的边界完成运行时修复：PE-01 取消游戏语言与编辑对象联动；PE-02 修正物理字典所有权及晚到注册预检；PE-03 统一原子编辑事务；PE/PF-04 删除编辑器字体实现，ProbablyFonts 保持可选软依赖；PF-01 修正整个启用期的回退引用清理；PF-02 分离文本分析与字体上下文缓存。REL-01 按用户决定暂缓，两库发布脚本保持独立。

无游戏验证结果：ProbablyEdited CoreChecks 30 项、ControllerChecks 11 项、LocalizationChecks 23 项通过；ProbablyFonts PureChecks 9 项通过。两库 Release 构建均为 0 警告、0 错误；ControllerChecks 编译仍有测试替身未使用事件的 CS0067 警告。未启动游戏，实际字体显示与 TMP 输入交互仍需人工验收。


## 结论

**两个仓库均未达到本轮严格可维护性审查的通过标准。** 共保留 **7 项核心问题：2 项 P1、5 项 P2**。优先处理编辑草稿与运行时字典的所有权问题，再收敛字体、输入事务及发布工具的重复实现。

本次审查的是两个 `main` 的完整代码树，不是最近一次提交的 diff。以下问题不一概归因于最近提交。大幅简化的方向是**让每种状态只有一个所有者**，而不是单纯拆文件、增加接口或把原有条件搬进更多类。

- **P1**：在列出的条件下存在用户编辑内容或其他来源文本被错误处理的风险，建议先于下一次发布解决。
- **P2**：明确的结构、生命周期或可避免计算问题，应在继续扩展相关模块前解决。
- 源码可直接确认的事实与未执行场景分开表述；后者标为 **[INFERENCE]**。

## 范围与证据

| 仓库 | 审查基线 | 生产 C# 文件 | 当前超过 1,000 行的生产文件 |
| --- | --- | ---: | --- |
| ProbablyEdited | `acb4f83b76c02357243193b5bc014b4fa209d6fa` | 24 | `EditorWindow.cs`：2,360；`TextInputContextMenu.cs`：1,147 |
| ProbablyFonts | `76775d87561ffcbc3fec609813885c0daf18f5c0` | 8 | 无 |

开始时，两个仓库的本地 `main`、`HEAD` 和 `origin/main` 均指向上述提交，工作区干净。未切换分支、拉取或更新远端。

审查覆盖生产代码、全部 C# / Python 测试源码、项目文件、GitHub Actions、发布脚本、清单、本地化资源及文档。图片资产仅检查引用与文件存在。由一个 reviewer 负责两库运行时与 C# 测试，主审负责发布、配置及文档资源，并核对核心问题的调用路径。

采用 Codegraph、源码及静态数据检查；本项目未配置 LSP。**没有运行测试、构建、游戏、DLL 检查或资源提取。唯一新增文件是本报告，未修改现有代码、配置或 README，也未提交、推送。**

## PE-01 · P1 — 编辑会话缺少统一所有者，外部语言变化绕过草稿处理

**位置：** [EditorController.cs:117–128](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/EditorController.cs#L117-L128)、[642–646](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/EditorController.cs#L642-L646)；[EditorWindow.cs:587–652](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/EditorWindow.cs#L587-L652)、[1505–1515](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/EditorWindow.cs#L1505-L1515)、[1722–1765](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/EditorWindow.cs#L1722-L1765)。

### 证据与后果

编辑目标分散在 Workspace 的选中预设、Controller 的 `SelectedLocale`，以及 Window 的 `_selectedEntry`、`_displayedLocale`、`_replacementDraft` 中。保存过程又分别由 Controller 的 `pendingApply` 与 Window 的 `_saveApplyPending`、`_saveApplyCallActive`、`_wasLoading` 描述。

窗口内的常规切换经过 `TryCommitCurrentDraft`，但游戏语言事件经 `LocaleChanged → LoadLocale` 直接改变目录和选中语言，不经过草稿事务：

- **[INFERENCE]** 窗口仍订阅事件且存在未暂存编辑时，如果目标语言已加载，`OnControllerChanged` 的重新绑定路径会用目标条目的文本替换草稿，并重设输入历史。
- **[INFERENCE]** 如果目标语言尚未加载，加载完成路径依赖仅由窗口切换入口设置的 `_awaitingLocale`。外部入口未设置它，可能留下旧语言的 `_selectedEntry`，却把显示上下文记录成新语言。

这不是增加一个“外部切换”布尔值就能长期解决的问题：当前每增加一种切换入口，都必须重新遵守一组散落的字段同步规则。

### 建议的结构调整

让一个编辑会话拥有当前 `(PresetId, TextId)`、未暂存草稿、待确认校验和切换结果。预设切换、文本语言切换及外部游戏语言通知统一进入这个边界，由会话决定是否改变编辑对象；Window 只显示结果。将加载／应用状态作为明确结果交给窗口，删除两端重复维护的保存状态标志。

保留“输入草稿、暂存、保存、生效”的区别，不以自动保存输入内容掩盖切换问题。若游戏语言变化本不应改变正在编辑的文本语言，应在这个边界直接保持编辑对象，而不是先切换再补救。

### 测试缺口

现有预设及历史测试没有执行 Window 的切换编排。ControllerChecks 的协程被同步排空，也无法覆盖真正挂起期间的草稿变化。需要覆盖已加载／未加载两条外部语言切换路径，以及校验窗口打开或保存待完成时的切换。

## PE-02 · P1 — 新建语言字典仍按语言名恢复，未固定实际写入对象

**位置：** [GameTextCatalog.cs:275–313](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/GameTextCatalog.cs#L275-L313)、[336–343](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/GameTextCatalog.cs#L336-L343)、[428–495](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/GameTextCatalog.cs#L428-L495)。

### 证据与后果

已有字典已经按 `(dictionary reference, key)` 共享物理目标，这是正确方向。但原本不存在的语言会创建 `TargetDictionary == null` 的目标；第一次 `Apply` 新建字典后，并未把实际字典登记回该目标。

此后 `Matches` 继续只比较语言名，`Restore` 重新读取当前 `Mod.Localization[Locale]`，而 `CleanupCreatedModLocales` 也只按语言名判断和删除。

**[INFERENCE]** 以下交接会清理错误的对象：加载缺失语言 → 应用覆盖并创建字典 D1 → 来源模组重新加载、把同一语言替换为 D2 → 恢复原文或停用编辑器。恢复可能删掉 D2 的同名键；若 D2 随后为空，还可能删除该语言映射。再次加载目录也不能自然修复，因为空目标模式仍把 D2 当作原目标。

### 建议的结构调整

第一次实际写入时就固定物理目标，新建字典与已有字典进入同一个引用身份索引。恢复记录直接持有实际字典、键和原始状态；移除新建语言映射前，必须确认当前映射仍指向本次创建的字典。

以一份写入所有权记录替代“已有字典按引用、新建字典按名称”的双模式，删除由空目标模式带来的特殊分支，而不是再叠加模组重载特判。

### 测试缺口

LocalizationChecks 确实直接编译生产 `GameTextCatalog`，共享别名、原始基线、冲突前置验证和新建语言清理的测试有效。不过，`ReloadsCreatedLocaleWithoutLosingBaseline` 没有在重载时替换字典实例。应补充 D1 → D2 交接，验证恢复／Dispose 不改 D2，且 D1 的清理由原所有者完成。

## PE-03 · P2 — 原子编辑作用域与实际历史提交边界不一致

**位置：** [TextInputContextMenu.cs:91–112](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/UI/TextInputContextMenu.cs#L91-L112)、[679–690](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/UI/TextInputContextMenu.cs#L679-L690)、[810–881](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/UI/TextInputContextMenu.cs#L810-L881)。

### 证据与后果

这个 1,147 行文件同时承担菜单呈现、TMP 事件桥接、历史回放、IME、剪贴板及事务控制。复杂度主要来自多套提交机制：`_applyingHistory`、`_applyingContextEdit`、延迟原子快照和 `_atomicEditDepth` 各自决定如何写历史。

`BeginAtomicEdit` 的公开契约是作用域内修改组成一个撤销事务；实现却在每次 `OnValueChanged` 中调用 `RecordAtomic`，而 `EndAtomicEdit` 只递减深度。

**[INFERENCE]** 同一作用域内出现两个不同文本通知时，会形成两个撤销步骤，嵌套作用域也没有独立的最外层提交点。当前清空搜索只有一次赋值，因此不能据此声称普通清空已失败；问题是现有可复用 API 的承诺不成立。

### 建议的结构调整

保留已有纯逻辑 `TextEditHistory`，在 TMP 适配侧只维护一份待提交编辑：起始快照、最终快照及编辑种类。显式作用域在最外层结束时提交；IME、剪贴板在各自处理边界提交；回放走单独的受控重入通道。最后统一到一次历史提交，删除平行的原子模式与重复 flush 规则，再把菜单绘制从输入事务适配中分离。

### 测试缺口

CoreChecks 对生产纯历史的覆盖有价值，但没有编译 `InputBinding` 或 Relay。应验证同一事务多通知、嵌套作用域、IME 提交前后切换编辑目标，以及过期的延迟选区恢复，而不只是再次直接调用 `RecordAtomic`。

## PE/PF-04 · P2 — 两库同时管理字体克隆，破坏单一所有权和原字体身份

**位置：** ProbablyEdited [NativeFontSupport.cs:278–328](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/src/UI/NativeFontSupport.cs#L278-L328)；ProbablyFonts [NativeFontCatalog.cs:57–90](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/src/NativeFontCatalog.cs#L57-L90)、[215–231](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/src/NativeFontCatalog.cs#L215-L231)。

### 证据与后果

ProbablyEdited 已声明 ProbablyFonts 为首项先决模组，却仍保留韩文补字探测、双向字体克隆缓存、主字体替换和下拉展开后的协程修复。ProbablyFonts 则对相同文本执行字体发现、角色判断、克隆及材质处理。

编辑器的补字路径会用含 `[primary, fallback]` 的克隆替换 `text.font`；字体模组的角色识别却依赖原生字体的引用身份。

**[INFERENCE]** 当这条补字路径触发且文本没有 `TMPLocalizer` 时，Caveat／Tektur 等字体的克隆无法再按原资产引用识别手写／科幻角色，可能落入 Standard 路径。即使具体文本仍能显示，修改任一侧的生命周期也必须理解另一侧的克隆和释放规则。

### 建议的结构调整

字体选择、克隆、材质、回退链统一由 ProbablyFonts 拥有。删除编辑器内部完整的字体克隆系统及散布的补字编排。确有必要的原生下拉生命周期修复也归字体模组，保留原字体身份作为输入。

不增加缺依赖时的运行时强制拦截；保持当前缺依赖警告与加载契约。本建议不是因为官方已修复 F8 就删除字体模组，而是删除编辑器中的第二个字体所有者。

### 测试缺口

两库当前检查工程均未覆盖这个组合路径。应验证带／不带 `TMPLocalizer` 的控件、下拉展开、字体角色识别，以及字体模组停用后的恢复；仅测各自的字体候选排序不足以证明协作正确。

## PF-01 · P2 — 保留重复全局引用，却立即销毁引用指向的字体

**位置：** [FallbackOwnership.cs:164–180](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/src/Core/FallbackOwnership.cs#L164-L180)、[FontRuntime.cs:137–144](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/src/FontRuntime.cs#L137-L144)、[FontCloneStore.cs:142–166](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/src/FontCloneStore.cs#L142-L166)、[PureChecks/Program.cs:143–158](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/tests/ProbablyFonts.PureChecks/Program.cs#L143-L158)。

### 证据与后果

`OwnedListEntries.Restore` 对每个自有引用只移除一次，然后忘记该引用；`FontRuntime.Stop` 随后销毁全部自有字体、材质和动态图集。

测试明确支持“其他修改者再次放入同一个引用”的情形，并要求恢复后保留重复项。但容器中保留引用，并不等于资源生命期已经转交。

**[INFERENCE]** 在这一已有测试描述的场景下，停用后全局回退列表仍会含指向已销毁克隆的项。具体渲染后果取决于 TMP 如何处理失效对象；可以确认的是列表保留策略与资源释放策略不一致。

### 建议的结构调整

由同一个启用期字体会话同时拥有克隆和全局挂载记录。最简单的契约是克隆不转交所有权：释放前，移除全局列表中所有指向本会话自有克隆的项，同时保留游戏及其他模组自己拥有的字体。

如果确实需要转交克隆，必须同时转交或延长资源生命期。不要把“保留重复列表元素”当作所有权转移。统一生命周期后，再收敛 `Suspend`、`Reconcile`、`Stop` 的重复清理步骤。

### 测试缺口

当前 `List<object>` 测试只证明保留了元素，未经过 `FontRuntime.Stop → FontCloneStore.Release`。应在生产所有权协调层验证重复引用、列表替换、场景重建与最终释放，检查留下的引用是否仍指向有效资源。

## PF-02 · P2 — 单槽字体上下文缓存反复重算全文，候选覆盖又计算两遍

**位置：** [FontRuntime.cs:339–434](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/src/FontRuntime.cs#L339-L434)。

### 证据与后果

每个 `TMP_Text` 只有一个 `TextSelectionCache`，缓存匹配却包括当前 `primaryFont`、角色和是否内联字体。文本在正文与内联字体间切换时，单槽被反复覆盖，每次都重新分析全文并选择三个脚本族。

每族已用唯一代码点检查候选覆盖，随后又调用 `SelectCompleteCandidate` 扫描原文并重新检查。共享 `codePointArray` 还要求长度与当前族完全相等，长度不同就重新分配。

**[INFERENCE]** 多内联字体段落的全文分析成本随“文本长度 × 字体上下文切换次数”增长；切换次数随文本增长时，可形成二次级重复工作。多个脚本族的不同长度又造成重复数组分配。本轮没有测量实际耗时或分配量。

### 建议的结构调整

将缓存拆成两个真正不同的寿命：按输入 `Revision` 保存一次文本分析与各族唯一代码点；按字体上下文缓存选择结果。候选循环既已发现首个完整覆盖者，就直接返回，并同时累计需要的缺字统计，删除后面的第二次选择扫描和中间候选列表。

代码点缓冲区按实际消费接口复用，不在三个脚本族之间用同一个精确长度数组反复改尺寸。目标是删除重复分析和复制，而不是泛化出新的缓存框架。

### 测试缺口

现有纯逻辑测试没有经过运行时缓存。可在生产选择协调逻辑的字体访问边界计数，验证同一 revision 只分析一次、字体上下文往返复用结果，并保持原有完整覆盖与字重选择行为。

## REL-01 · P2 — 两份几乎完全相同的发布引擎，应有单一维护源

**位置：** 两库各自的 `scripts/release.py`，尤其 [ProbablyEdited:29–45](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/scripts/release.py#L29-L45)、[772–870](https://github.com/Octobersama/ProbablyEdited/blob/acb4f83b76c02357243193b5bc014b4fa209d6fa/scripts/release.py#L772-L870)，以及 [ProbablyFonts:772–870](https://github.com/Octobersama/ProbablyFonts/blob/76775d87561ffcbc3fec609813885c0daf18f5c0/scripts/release.py#L772-L870)。

### 证据与后果

两个文件都是 920 行，静态逐行比较只有 **4 行不同**：模块说明、项目名、项目文件名和一个工作流提示。发布测试也基本复制，仅调整项目名与 ZIP 示例内容。

重复部分并非简单打包命令，而是包含认证头处理、重定向保护、CI 提交身份校验、Git 提交／推送、带注释 tag、草稿发布、附件下载校验等维护敏感的逻辑。**[INFERENCE]** 修复任一安全或发布行为时都必须同步维护两份实现与测试；两个独立模组仓库并不要求这些通用机制也分叉。

### 建议的结构调整

保留两库独立构建及现有 CLI，给发布引擎建立一个维护源，以固定版本工具或受控同步的 vendored 模块分发；项目差异仅保留小型配置／入口。不要让 ProbablyFonts 通过相对路径导入 ProbablyEdited，避免破坏仓库可独立移动的要求。

内部只需要清晰区分“从确定候选生成发布产物”和“操作 GitHub 发布”两个边界，不需要新建通用发布平台。保留精确提交检查、不会覆盖既有发布、草稿上传成功后才公开等现有保护。

### 测试缺口

目前对 `publish_release` 的唯一测试调用在“已有草稿”处提前拒绝；未测试完整成功路径。`prepare_local`、`write_artifacts`、tag 协调和附件校验也没有入口级组合覆盖。

共享实现时，应以临时本地 Git 仓库和可替换的构建／GitHub 边界验证完整顺序，以及 CI 失败不创建 tag、上传／校验失败不公开 Release。无需真实推送或发布来测试这些规则。

## 测试架构判断

### 应保留的有效覆盖

- ProbablyEdited 的 CoreChecks、PresetChecks、DiffChecks 直接编译生产纯代码，适合保护预设隔离、历史分组、差异算法与旧格式兼容性。
- LocalizationChecks 直接编译生产 `GameTextCatalog`；字典、表和加载句柄等引擎替身提供了必要外部能力，不是复制生产目录规则。共享别名和冲突前置验证属于有价值的回归测试。
- ProbablyFonts 对输入切片快照、脚本选择、候选完整覆盖的纯逻辑检查有价值。
- 发布测试覆盖真实 ZIP 成员及成员字节哈希、部分认证保护和限定文件提交；这些保护应保留。

### 不应当作完整运行时证据的部分

ProbablyEdited `ControllerChecks/TestDoubles.cs:182–317` 定义了另一份同名 `GameTextCatalog`，自己实现加载、应用和部分失败；`153–161` 同步排空协程。这能检查部分 Controller 编排，但不能证明生产目录与 Controller 的组合在挂起、重载或别名冲突期间正确。

后续应把替换边界放到存储、引擎访问与可步进调度，复用生产目录／事务逻辑，而不是不断扩充第二份业务实现。字体运行时与 TMP 输入适配也应采用同样原则：测试生产协调逻辑，不要求把整个 Unity 场景搬进测试。

## 其他静态检查结果

- 9 个本地化 CSV 均有 372 个键；未发现重复键、缺失／额外键、空值或编号占位符集合不一致。此检查不等价于翻译质量验收或实机字体显示验证。
- 三份预设格式文档的 JSON 示例均能解析；文档中的本地链接及图片引用未发现缺失目标。
- 两库许可证文本在换行／BOM 归一后相同，均为填写了 `2026 OctoberSama` 的 Apache 2.0；NOTICE、主项目版本和清单版本对应当前 `1.0.0`。
- 两库清单均保留 `0.46D` 与 `1.0.0`；ProbablyEdited 的首项先决模组为 `probably_fonts`。项目文件明确不复制游戏引用程序集。
- Actions 当前执行检查工程并上传文本结果；完整模组构建在本地发布流程完成。这与当前 `docs/releasing.md` 的说明一致，不能把 Actions 成功理解为已经编译所有 Unity 集成代码。

## 覆盖清单

### ProbablyEdited

- 根运行时：`ModEntry`、`EditorController`、`EditorWindow`、`GameTextCatalog`、`CatalogPresentation`、`NativeGameLanguageChoices`、`UiLocalization`。
- Core：`TextId`、`TextEntry`、`EditorState`、`PresetWorkspace`、`ImportPlan`、`TextEditHistory`、`TextHistoryShortcut`、`TextDiff`、`SourceFilterChoices`、`ModLocalizationAliases`、`UiLocaleCatalog`、`UiText`。
- UI：`EditorWindow.Presets`、`TextInputContextMenu`、`NativeFontSupport`、`ResultRowSelection`、`TextDiffHighlighter`。
- 测试：五个 C# 检查工程的全部源码／项目文件、共享本地化测试支持、Python 发布测试。
- 其他：主项目、manifest、workflow、发布脚本、9 个 CSV、README、CHANGELOG、三语言预设格式文档、发布指南、LICENSE、NOTICE、Git 排除规则；图片引用检查。

### ProbablyFonts

- 运行时：`ProbablyFontsMod`、`FontRuntime`、`FontCloneStore`、`NativeFontCatalog`、`FontInputSnapshots`、`Core/FontSelection`、`Core/TextInputSnapshot`、`Core/FallbackOwnership`。
- 测试：PureChecks 全部源码／项目文件、Python 发布测试。
- 其他：主项目、manifest、workflow、发布脚本、README、CHANGELOG、发布指南、LICENSE、NOTICE、Git 排除规则；图片引用检查。

稳定 TextId、命名预设及迁移、导入预览失效检查、保留未知语言覆盖、来源选择和纯文本差异算法，没有足够证据再列独立核心问题。大文件是职责失衡的信号；本报告没有把“超过 1,000 行”本身当作功能缺陷，也没有认定最近提交造成了跨阈值增长。

## 官方资料依据

以下原始资料读取日期均为 **2026-11-13**：

- [官方模组指南](https://raw.githubusercontent.com/dragonpan2/probably-stolen-modding/main/ModdingGuide.md)：资源加载生命周期、官方字典、UI／Options、Harmony 目标及先决模组规则。
- [官方 API 变更记录](https://raw.githubusercontent.com/dragonpan2/probably-stolen-modding/main/APIChanges.md)：`files`／`network` 开发者预审要求已撤销；玩家权限提示与其他限制继续实施。具名 TMP／UI 补丁仍在允许范围。线上 100-Rev14 的 F8 字体修复不等于全部全局字重问题已修复。
- [官方行为准则](https://raw.githubusercontent.com/dragonpan2/probably-stolen-modding/main/CodeOfConduct.md)：最小必要能力声明及不得再分发游戏代码／资产。

上述资料用于判断架构与接口边界，不代替已安装客户端的兼容性验证。本轮没有重新出具 `mod-scan` 或实机通过结论。
