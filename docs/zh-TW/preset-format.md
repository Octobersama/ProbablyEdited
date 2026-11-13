# 匯出的預設集 JSON 格式

Probably Edited 會將**單一預設集的覆寫內容**匯出成共用檔案。此檔案是修補檔，不是 `presets.json` 工作區快照：它不包含其他預設集、啟用中的預設集 ID、預設集 ID 或遊戲原始文字。

## 支援的檔案版本

目前的單一預設集匯出器會寫入 `schemaVersion: 2`。匯入器也接受舊版修補檔 `schemaVersion: 1`。

| 版本 | 根物件成員（全部必填） | 預設集名稱 |
| --- | --- | --- |
| `2` — 目前匯出格式 | `schema`、`schemaVersion`、`gameVersion`、`presetName`、`overrides` | 必填；從檔案建立新預設集時會用作名稱。 |
| `1` — 舊版修補檔 | `schema`、`schemaVersion`、`gameVersion`、`overrides` | 不存在。以檔案建立新目標時會使用下一個預設名稱；合併到既有目標時會保留目標原有名稱。 |

兩個版本都使用 `schema: "probably-edited"`，並採用下方說明的相同覆寫記錄。工作區儲存資料使用獨立的 `probably-edited-workspace` schema，版本為 1–3；它不是共用檔案版本，也無法當成共用檔案匯入。

匯出器會寫入縮排為兩個空格的 JSON，並依 `sourceId`、`locale`、`tableId`、`key` 的序數順序排序覆寫記錄。匯入時陣列順序不影響比對。

## 欄位與驗證規則

JSON 成員名稱有大小寫之分。成員順序不重要，但每個物件必須且只能包含該版本規定的成員：缺少成員或出現額外成員都會遭到拒絕。

| 欄位 | 型別 | 必填 | 意義與規則 |
| --- | --- | --- | --- |
| `schema` | 字串 | 是 | 必須完全等於 `probably-edited`。 |
| `schemaVersion` | 整數 | 是 | 必須是 `2` 或 `1`；不支援其他版本。 |
| `gameVersion` | 字串 | 是 | 匯出當下由 `GameTextCatalog` 使用的 `PlayerStore.VERSION` 值，用來表示遊戲在地化內容修訂版。它不是模組資訊清單的 `<GameVersions>` 相容性清單，也不是 `<ModVersion>`（`1.0.0`）。解析器接受任何字串，包括空字串。匯入時版本不符會提出警告，但不會阻止匯入；若目前值無法取得，預覽會提示無法檢查。 |
| `presetName` | 字串 | 僅限版本 2 | 匯出預設集的名稱。不得為空或只含空白字元。匯入為新預設集時會去除前後空白；匯入到既有目標時會保留該目標的名稱。 |
| `overrides` | 陣列 | 是 | 可以是空陣列。每個元素代表一筆替換，且必須包含下方五個字串欄位。 |

`overrides` 中的每個項目都必須且只能包含下列欄位：

| 欄位 | 型別 | 意義與規則 |
| --- | --- | --- |
| `sourceId` | 字串 | 來源識別碼。遊戲在地化資料表使用 `game`（包括註冊到該資料庫的第三方資料表，其原始模組來源無法可靠歸屬）；模組在地化使用模組資訊清單中的 ID。 |
| `locale` | 字串 | 項目的確切語言／地區代碼，例如 `en`、`fr`、`ptBR` 或 `pt-BR`。讀取時不會正規化代碼；`ptBR` 和 `pt-BR` 是不同識別碼。 |
| `tableId` | 字串 | 資料表識別碼。遊戲在地化資料表使用 `N` 格式的資料表集合 GUID；模組 CSV 在地化使用 `Localization`。 |
| `key` | 字串 | 該來源、語言及資料表中的項目識別碼。遊戲資料表使用以字串寫入的共用資料數字 key ID；模組 CSV 在地化使用 CSV 中的確切 key。 |
| `text` | 字串 | 替換文字。允許並會保留空字串作為覆寫；`null` 會遭拒。遊戲資料表中的空值可能觸發 Unity 語言回退，因此不能可靠地用來清除或隱藏顯示文字。 |

四個識別欄位（`sourceId`、`locale`、`tableId`、`key`）都必須至少包含一個非空白字元。項目會依完整四元組 `(sourceId, locale, tableId, key)` 精確比對，並區分大小寫。讀取時不會修剪空白或轉換大小寫。同一檔案中重複的四元組會視為重複覆寫並遭到拒絕。

ProbablyEdited 自身的翻譯資源使用 `en-US.csv`、`zh-CN.csv` 等語言－地區檔名，但載入後會在官方字典中保留指向同一份資料的遊戲代碼別名（例如 `en`、`zh`）。因此，依遊戲原生語言編輯本模組文字時，預設項目的 `locale` 仍使用原本的遊戲代碼；不要因為 CSV 更名就改寫既有 JSON 中的語言識別碼。

匯出器會輸出標準 JSON。匯入器使用 Json.NET 讀取，並明確要求根值為物件、拒絕註解 token、重複 JSON 成員名稱及第一個 JSON 值之後的內容，接著檢查上文列出的欄位名稱與型別。Json.NET 接受某些語法擴充，因此不能將此匯入器視為嚴格驗證 JSON 標準所有詞法規則的解析器。空的 `overrides` 陣列有效；解析器沒有為覆寫數量、JSON 大小或字串長度設定固定上限。

## 最小有效的版本 2 檔案

下列檔案在結構上有效，且不包含任何文字變更：

```json
{
  "schema": "probably-edited",
  "schemaVersion": 2,
  "gameVersion": "example-game-version",
  "presetName": "範例預設集",
  "overrides": []
}
```

## 含替換內容的範例

```json
{
  "schema": "probably-edited",
  "schemaVersion": 2,
  "gameVersion": "example-game-version",
  "presetName": "範例在地化修改",
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

`example.mod` 和這些 key 都是虛構且固定的範例，不是遊戲資料摘錄。它們在結構上有效，但只有完整識別碼完全相同的項目才會比對成功。`\r\n` 和 `\n` 會解碼成不同的換行；匯入器不會正規化換行。Unicode、標籤、大括號和其他文字都會保留為字串內容。空的 `text` 是明確存在的覆寫；其執行階段顯示效果取決於來源。

編輯器的文字檢查會在佔位符 token 或富文字標籤名稱與本機原文不一致時提出提示，並檢查基本的大括號平衡。匯入 JSON **不會**執行該檢查，也不會剖析文字使用的格式語言。範例中的 `example.mod` `Localization` 記錄採用 CSV 的編號佔位符形式，例如 `{0}`。遊戲字串表則可能使用 Unity Localization Smart String，其變數、條件及複數格式語法各自不同；檔案只保存文字，不含 Smart String 標記。請保留來源項目所需的語法；JSON schema 無法保證任意替換文字都能正確顯示。

## 舊版 1 範例

版本 1 沒有 `presetName` 成員：

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

版本 1 仍支援匯入舊版修補檔。新的單一預設集匯出會使用版本 2。

## 匯入行為

1. 先預覽檔案，再選擇既有預設集或新預設集作為目標。預覽新目標時不會立即建立預設集。
2. 匯入器會依完整四部分識別碼比對每個項目，並合併至所選目標。檔案中不存在的項目會保留；檔案不會取代或清除目標預設集。
3. 若目標已覆寫相同識別碼但文字不同，即為衝突。文字相同不算衝突。選擇保留目標的值，或以匯入文字覆寫衝突值；無論選擇哪一種方式，非衝突項目都會合併。
4. 確認後，合併內容會暫存於工作區。它不會立即變更遊戲目前套用的文字，也不會儲存工作區；請使用 **Save and Apply**（儲存並套用）來儲存及套用。

匯入器會列出模組資料目錄中的 `.json` 檔案，但會排除根目錄下的工作區與舊版檔案：`presets.json`、`presets.previous.json`、`active.json` 和 `previous.json`。

新匯入的預設集會使用版本 2 的 `presetName`（會去除前後空白）；版本 1 則使用下一個預設名稱。工作區最多可有 36 個預設集；匯入新預設集需要尚有容量，但即使容量已滿，仍可合併至既有預設集。若預覽後工作區或所選目標有變更，請重新預覽檔案。

檔案中的 `gameVersion` 和來源 ID 用於識別與提供背景資訊，不是依賴項指令。檔案不包含依賴模組清單、模組顯示名稱、模組版本或原始文字。匯入器不會下載或安裝來源。缺少的來源或項目會保留為覆寫；不可用數量只會針對目前已載入語言的目錄檢查，不會把其他語言計為缺失。若需要解析對應項目，請另外安裝或啟用相關來源。

匯出 UI 另要求檔案名稱不得超過 80 個 UTF-16 程式碼單位且不可重複，並會將共用檔案儲存至模組的 `exports` 資料夾。此檔案名稱規則與 JSON 格式無關；JSON 匯入器沒有固定的覆寫數量或文字長度上限。
