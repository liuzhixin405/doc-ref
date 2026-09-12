# 知识库（JSONL）

本目录是 docref 的**共享知识库**。每个文档是一对文件：

- `<名字>.sections.jsonl` —— 正文，一行一个 section
- `<名字>.manifest.json` —— 溯源信息、提取警告、统计

clone 本仓库即可拿到一份可用知识库，直接检索：

```bash
docref kb "SqliteConnection" --dir <本目录>
docref kb --coverage --dir <本目录>
```

`sync-skills.py` 会把 skill 里的 `--dir` 默认指向本目录（相对脚本位置 `../kb` 算，
所以 clone 到哪台机器、哪个盘符都成立）。

## 两类文档

| 类型 | 前缀 | `manifest.source.producer` | 页码 | 怎么来的 |
|---|---|---|---|---|
| PDF 提取 | `dotnet-*` / `aspnet-*` | `Microsoft Learn PDF …` | 真实 PDF 页码 | `docref extract` 从官方 PDF 生成 |
| 联网同步 | `web-*` | `web-sync` | 合成的虚拟页码 | `sync-web-kb.py` 把联网搜索（L1/L2）结果手工沉淀 |

**引用约定（别混标）**：PDF 提取的可标 `[来源: <id> p<页码>]`（能翻回 PDF 核对）；
`web-*` 的只能标 `[联网-官方: URL]` / `[联网-非官方: URL]` —— 它的页码是合成的，
翻不到任何 PDF，标成 `[来源:]` 会骗过审查的人。

## 怎么加

- **加官方 PDF**：`docref extract "G:/pdf/c#/某个新文档.pdf" -o <本目录>`
  （整个目录批量：`docref extract "G:/pdf/c#" -o <本目录>`）
- **加联网知识**：`python dotnet-kb/sync-web-kb.py`（内置示例）
  或 `python dotnet-kb/sync-web-kb.py <输入.json>`（结构见脚本头部注释）

## 换电脑 / 给别人用

1. clone 本仓库 → 本目录自带全部 JSONL
2. 装好 docref（整个 publish 目录，见根 README）
3. 跑一次 `python dotnet-kb/sync-skills.py` → skill 的 `--dir` 指向这里的 `kb/`
4. 完事，`docref kb` 直接可查
