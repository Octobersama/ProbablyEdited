# 自动发布 ProbablyEdited

发布在本机完成，因为完整构建需要已安装游戏的程序集。GitHub Actions 负责自动测试，不在云端构建模组。需要 Python 3.9+、.NET SDK 和 Git。

## 1. 手写更改日志

在仓库根目录的 `CHANGELOG.md` 中，为准备发布的版本填写一节。可以把已有 `Unreleased` 内容整理为版本条目；例如：

```markdown
## 1.0.1

### 变更 / Changed
- 本地化资源改用 en-US、zh-CN 等标准文件名，保留游戏原生语言标识和文本枚举。
```

二级标题必须恰好是 `## X.Y.Z` 或 `## X.Y.Z-prerelease`，每个版本只能出现一次，内容不能为空。不要手填哈希表；脚本会自动追加。

## 2. 先在本机预览

在仓库根目录运行：

```powershell
python scripts/release.py --version 1.0.1
```

脚本会同步项目文件中的 `Version` 和清单中的 `ModVersion`，在临时目录构建 Release，再打包该构建生成的整个 `dist/`。ZIP 保留模组文件夹结构，不会只打包 DLL。

结果位于：

- `build/releases/1.0.1/ProbablyEdited-1.0.1.zip`
- `build/releases/1.0.1/release-notes.md`

发布说明保留你的更改日志，并列出 ZIP 内每个文件的完整 **SHA-512 和 MD5**；不计算或列出 ZIP 本身的哈希。这个命令不访问 GitHub，也不需要令牌。

游戏不在默认位置时，加上安装目录：

```powershell
python scripts/release.py --version 1.0.1 --game-directory "D:\SteamLibrary\steamapps\common\Probably Stolen"
```

## 3. 一条命令正式发布

先按正常开发流程提交并推送需要发布的代码，让当前分支与 GitHub 的 `origin` 分支一致。配置环境变量 `GITHUB_PERSONAL_ACCESS_TOKEN`：需要仓库 **Contents 读写**及 **Actions 读取**权限。不要把令牌写进仓库。

```powershell
python scripts/release.py --version 1.0.1 --publish
```

加上 `--publish` 后，脚本会：

1. 检查版本、日志、远端和已有发布，避免覆盖既有版本。
2. 同步版本，并只提交 `ProbablyEdited.csproj`、`manifest.xml`、`CHANGELOG.md` 这三个管理文件，然后推送。
3. 等待同一个提交的 `console-checks.yml` 检查成功。只接受测试该提交的 push／手动运行，不用 PR 合并提交的结果代替。
4. 从该提交构建并生成 ZIP、逐文件哈希表和发布说明。
5. 创建或核对带注释的 `v1.0.1` tag，上传附件到 Release 草稿，回下载核对字节后再正式发布。

README 和其他工作区／暂存区改动不会被擅自提交或丢弃；它们也不会进入此次发布构建。要发布的功能代码必须先提交。HTTPS 远端通过仅对子进程生效的认证头使用令牌，不写 Git 配置、不打印令牌；SSH 远端使用已有 SSH 配置。

若有意使用 `[no ci]` 等标记跳过了测试，脚本不会当作通过。请在 Actions 页面为未改变的分支 HEAD 手动运行 **Editor console checks**，成功后重试。

预发布版本可使用 `--version 1.0.1-rc.1`，并填写对应更改日志。脚本不会覆盖已有正式 Release、草稿或不同提交的 tag。若网络故障留下 tag 或草稿，请先检查并人工处理，再重试；脚本不会擅自删除这些远端内容。

游戏 DLL 只用于本机编译，不应放进 ZIP 或上传到 Actions。
