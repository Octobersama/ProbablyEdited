# 导出的预设 JSON 格式

Probably Edited 会将**单个预设的覆盖文本**导出为共用文件。此文件是补丁，不是 `presets.json` 工作区快照：它不包含其他预设、活动预设 ID、预设 ID 或游戏原始文本。

## 支持的文件版本

目前的单预设导出器会写入 `schemaVersion: 2`。导入器也接受旧版补丁文件 `schemaVersion: 1`。

| 版本 | 根对象成员（全部必填） | 预设名称 |
| --- | --- | --- |
| `2` — 当前导出格式 | `schema`、`schemaVersion`、`gameVersion`、`presetName`、`overrides` | 必填；从文件新建预设时用作名称。 |
| `1` — 旧版补丁 | `schema`、`schemaVersion`、`gameVersion`、`overrides` | 不存在。以文件新建目标时会使用下一个默认预设名称；合并到现有目标时保留目标原名称。 |

两个版本都使用 `schema: "probably-edited"`，并使用下文所述的同一种覆盖记录。工作区保存数据使用独立的 `probably-edited-workspace` schema，版本为 1–3；它不是共用文件版本，也不能作为共用文件导入。

导出器会写入缩进为两个空格的 JSON，并按 `sourceId`、`locale`、`tableId`、`key` 的序数顺序排序覆盖记录。导入时数组顺序不影响匹配。

## 字段与验证规则

JSON 成员名称区分大小写。成员顺序不限，但每个对象必须且只能包含对应版本规定的成员：缺少成员或出现额外成员都会被拒绝。

| 字段 | 类型 | 必填 | 含义与规则 |
| --- | --- | --- | --- |
| `schema` | 字符串 | 是 | 必须严格等于 `probably-edited`。 |
| `schemaVersion` | 整数 | 是 | 必须为 `2` 或 `1`；其他版本不受支持。 |
| `gameVersion` | 字符串 | 是 | 导出时由 `GameTextCatalog` 使用的 `PlayerStore.VERSION` 值，用于表示游戏本地化内容修订版。它不是模组清单的 `<GameVersions>` 兼容性列表，也不是 `<ModVersion>`。解析器接受任意字符串，包括空字符串。导入时版本不一致会警告，但不会阻止导入；若当前值不可用，预览会提示无法检查。 |
| `presetName` | 字符串 | 仅版本 2 | 导出的预设名称。不得为空或仅包含空白字符。作为新预设导入时会去除首尾空白；导入到现有目标时保留该目标原名称。 |
| `overrides` | 数组 | 是 | 可以为空。每个元素代表一项替换，且必须包含下列五个字符串字段。 |

`overrides` 中的每个元素都必须且只能包含以下字段：

| 字段 | 类型 | 含义与规则 |
| --- | --- | --- |
| `sourceId` | 字符串 | 来源标识。游戏本地化表使用 `game`（包括注册到该数据库的第三方表，其原始模组来源无法可靠归属）；模组本地化使用模组清单中的 ID。 |
| `locale` | 字符串 | 条目的确切语言/区域代码，例如 `en`、`fr`、`ptBR` 或 `pt-BR`。读取时不会规范化代码；`ptBR` 和 `pt-BR` 是不同的标识。 |
| `tableId` | 字符串 | 表格标识。游戏本地化表使用 `N` 格式的表格集合 GUID；模组 CSV 本地化使用 `Localization`。 |
| `key` | 字符串 | 在该来源、语言和表格中的条目标识。游戏表格使用以字符串形式写入的共享数据数字 key ID；模组 CSV 本地化使用 CSV 中的确切 key。 |
| `text` | 字符串 | 替换文本。允许并保留空字符串作为覆盖；`null` 会被拒绝。游戏表格中的空值可能触发 Unity 语言回退，因此不能可靠地用来清除或隐藏显示文字。 |

四个标识字段（`sourceId`、`locale`、`tableId`、`key`）都必须至少包含一个非空白字符。条目通过完整四元组 `(sourceId, locale, tableId, key)` 精确匹配且区分大小写。读取时不会修剪空白或转换大小写。同一文件中重复的四元组会被视为重复覆盖并拒绝。

ProbablyEdited 自身的翻译资源使用 `en-US.csv`、`zh-CN.csv` 等语言－地区文件名，但加载后会在官方字典中保留指向同一份数据的游戏代码别名（例如 `en`、`zh`）。因此，按游戏原生语言编辑本模组文本时，预设条目的 `locale` 仍使用原来的游戏代码；不要因为 CSV 改名就改写已有 JSON 中的语言标识。

如果同一预设通过多个语言别名指向同一个运行时字典键（例如本模组的 `en` 和 `en-US`），相同替换文本可以一起应用；不同替换文本会使“保存并应用”失败，原先生效的文本保持不变。请只保留一个别名的覆盖，或统一替换文本后重试。这项检查发生在应用阶段，与导入预览按完整四元组统计的冲突不同。

导出器生成标准 JSON。导入器使用 Json.NET 读取，并显式要求根值为对象、拒绝注释 token、重复 JSON 成员名称以及第一个 JSON 值之后的内容，然后检查上文所列的字段名称和类型。Json.NET 接受某些语法扩展，因此不能将此导入器视为严格验证 JSON 标准全部词法规则的解析器。空 `overrides` 数组有效；解析器没有为覆盖数量、JSON 大小或字符串长度设定固定上限。

## 最小有效的版本 2 文件

下列文件在结构上有效，且不包含任何文本变更：

```json
{
  "schema": "probably-edited",
  "schemaVersion": 2,
  "gameVersion": "example-game-version",
  "presetName": "示例预设",
  "overrides": []
}
```

## 含替换内容的示例

```json
{
  "schema": "probably-edited",
  "schemaVersion": 2,
  "gameVersion": "example-game-version",
  "presetName": "示例本地化修改",
  "overrides": [
    {
      "sourceId": "example.mod",
      "locale": "en",
      "tableId": "Localization",
      "key": "welcome",
      "text": "<b>Welcome, {0}</b>\r\nChoose a route.\n"
    },
    {
      "sourceId": "example.mod",
      "locale": "fr",
      "tableId": "Localization",
      "key": "farewell",
      "text": "À bientôt."
    },
    {
      "sourceId": "example.mod",
      "locale": "en",
      "tableId": "Localization",
      "key": "intentionally-empty",
      "text": ""
    }
  ]
}
```

`example.mod` 和这些 key 都是虚构且固定的示例，并非游戏资料摘录。它们在结构上有效，但只有在完整标识完全相同的条目存在时才会匹配。`\r\n` 和 `\n` 会解码为不同的换行符；导入器不会规范化换行。Unicode、标签、大括号和其他文字都会作为字符串内容保留。空 `text` 是明确存在的覆盖；其运行时显示效果取决于来源。

编辑器的文本检查会在占位符 token 或富文本标签名称与本地原文不一致时给出提示，并检查基本的大括号平衡。导入 JSON **不会**执行该检查，也不会解析文本所使用的格式语言。示例中的 `example.mod` `Localization` 记录采用 CSV 的编号占位符形式，例如 `{0}`。游戏字符串表则可能使用 Unity Localization Smart String，其变量、条件和复数格式语法各自不同；文件只保存文本，不含 Smart String 标记。请保留来源条目所需的语法；JSON schema 无法保证任意替换文字都能正确显示。

## 旧版 1 示例

版本 1 没有 `presetName` 成员：

```json
{
  "schema": "probably-edited",
  "schemaVersion": 1,
  "gameVersion": "example-game-version",
  "overrides": [
    {
      "sourceId": "example.mod",
      "locale": "en",
      "tableId": "Localization",
      "key": "welcome",
      "text": "Welcome, {0}!"
    }
  ]
}
```

版本 1 仍支持导入旧版补丁文件。新的单预设导出使用版本 2。

## 导入行为

1. 先预览文件，再选择现有预设或新预设作为目标。预览新目标时不会立即创建预设。
2. 导入器按四部分完整标识匹配每个条目，并将其合并到所选目标。文件中不存在的条目保持不变；文件不会取代或清空目标预设。
3. 如果目标中已覆盖相同标识但文字不同，则发生冲突。文字相同不算冲突。选择保留目标值，或以导入文字覆盖冲突值；无论选择哪种方式，非冲突条目都会合并。
4. 确认后，合并内容会暂存于工作区。它不会立即改变游戏当前套用的文字，也不会保存工作区；请使用 **Save and Apply**（保存并套用）来保存及套用。

导入器会列出模组数据文件夹中的 `.json` 文件，但会排除根目录下的工作区和旧版文件：`presets.json`、`presets.previous.json`、`active.json` 和 `previous.json`。

新建的导入预设会使用版本 2 的 `presetName`（已修剪首尾空白）；版本 1 则使用下一个默认名称。工作区最多允许 36 个预设；导入新预设需要仍有容量，但即使容量已满，仍可合并至现有预设。如果预览后工作区或所选目标发生变更，请重新预览文件。

文件中的 `gameVersion` 和来源 ID 用于识别与提供背景信息，并非依赖项指令。文件不包含依赖模组清单、模组显示名称、模组版本或原始文本。导入器不会下载或安装来源。缺少的来源或条目会作为覆盖保留；不可用数量只会针对目前已载入语言的目录进行检查，不会把其他语言计为缺失。若需要解析对应条目，请自行安装或启用相关来源。

导出 UI 另要求文件名称不超过 80 个 UTF-16 代码单元且不得重复，并会将共用文件保存至模组的 `exports` 数据文件夹。此文件名称规则与 JSON 格式无关；JSON 导入器没有固定的覆盖数量或文本长度上限。
