#!/usr/bin/env python3
"""sync-web-kb.py —— 把联网搜到的知识同步进知识库（JSONL 格式）。

本文件是 skill 的一部分，由 dotnet-kb/sync-skills.py 部署到 skill 目录；
部署时由 sync-skills.py 把知识库目录占位符填成实际路径（默认是仓库内的 kb/）。

背景：dotnet-kb 的四级阶梯里，L1/L2 是联网结果。本脚本把这些联网结果落盘到
知识库，形成一份「web-sync」文档，下次直接本地命中，省得再联网。

与官方 PDF 提取的文档明确区分（保真起见，避免把合成页码冒充成可核对 PDF 页码）：
  - manifest.source.producer = "web-sync"（官方 PDF 是 "Microsoft Learn PDF ..."）
  - manifest.source.file 记来源 URL
  - 每个 section 的第一个 block 是来源声明（含 URL），搜索摘要里直接可见
  - pages 是合成的虚拟页码；引用时请标 [联网-官方:]/[联网-非官方:]，不要标 [来源: id p页码]

用法：
  python sync-web-kb.py                用内置示例（Microsoft.Data.Sqlite CRUD）写库
  python sync-web-kb.py <输入.json>    从 JSON 输入文件读文档定义写库

输入 JSON 结构：
{
  "stem": "web-<主题>",                # 建议 web- 前缀，一眼看出是联网同步
  "title": "<文档标题>",
  "source_url": "<默认来源URL>",        # 可被 section 级覆盖
  "sections": [
    {
      "title": "<小节标题>",
      "source_url": "<本节来源URL>",    # 可选，覆盖文档级
      "blocks": [
        {"kind": "text", "text": "..."},
        {"kind": "code", "text": "..."}
      ]
    }
  ]
}
"""

import json
import sys
from datetime import datetime, timezone

# 知识库目录；部署时由 sync-skills.py 填入实际路径。
KB_DIR = "@KB@"

DEMO_DOC = {
    "stem": "web-microsoft-data-sqlite",
    "title": "Microsoft.Data.Sqlite（联网同步）",
    "source_url": "https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/",
    "sections": [
        {
            "title": "概述与安装",
            "source_url": "https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/",
            "blocks": [
                {"kind": "text", "text": "Microsoft.Data.Sqlite 是轻量级 ADO.NET SQLite 提供程序。EF Core 的 SQLite 提供程序构建在它之上，也可以独立使用。"},
                {"kind": "code", "text": "dotnet add package Microsoft.Data.Sqlite"},
                {"kind": "text", "text": "该包会安装 Microsoft.Data.Sqlite.Core 作为依赖，并带入 SQLitePCLRaw 原生绑定。"},
            ],
        },
        {
            "title": "连接串与 SqliteConnection",
            "source_url": "https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/",
            "blocks": [
                {"kind": "text", "text": "连接串格式 \"Data Source=<文件路径>\"。用 SqliteConnection 打开，用 connection.CreateCommand() 创建命令。"},
                {"kind": "code", "text": 'using var connection = new SqliteConnection("Data Source=hello.db");\n\nconnection.Open();\n\nusing var command = connection.CreateCommand();'},
            ],
        },
        {
            "title": "参数化查询（命名参数 $）",
            "source_url": "https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/",
            "blocks": [
                {"kind": "text", "text": "命名参数用 $ 前缀，通过 Parameters.AddWithValue(\"$id\", id) 绑定。不要拼接用户输入到 SQL，避免注入。"},
                {"kind": "code", "text": 'command.CommandText = """\n    SELECT name\n    FROM user\n    WHERE id = $id\n""";\ncommand.Parameters.AddWithValue("$id", id);'},
            ],
        },
        {
            "title": "执行方法：NonQuery / Scalar / Reader",
            "source_url": "https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlite.sqlitecommand",
            "blocks": [
                {"kind": "text", "text": "SqliteCommand 三个执行方法：ExecuteNonQuery 用于无结果集语句（建表/INSERT/UPDATE/DELETE），返回受影响行数；ExecuteScalar 返回单个值；ExecuteReader 返回数据读取器。"},
                {"kind": "code", "text": "int rows = command.ExecuteNonQuery();        // 增删改\nobject value = command.ExecuteScalar();     // 单个值\nusing var reader = command.ExecuteReader(); // 结果集"},
            ],
        },
        {
            "title": "读取结果（SqliteDataReader）",
            "source_url": "https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlite.sqlitedatareader",
            "blocks": [
                {"kind": "text", "text": "SqliteDataReader.Read() 前进到下一行；GetString(n) 读 TEXT，GetInt64(n) 读 INTEGER（SQLite 整数是 64 位）。"},
                {"kind": "code", "text": 'using var reader = command.ExecuteReader();\n\nwhile (reader.Read())\n{\n    var name = reader.GetString(0);\n\n    Console.WriteLine($"Hello, {name}!");\n}'},
            ],
        },
        {
            "title": "last_insert_rowid()",
            "source_url": "https://www.sqlite.org/lang_corefunc.html",
            "blocks": [
                {"kind": "text", "text": "SQLite 内置函数 last_insert_rowid() 返回同一连接上最近一次 INSERT 的 rowid，可用 ExecuteScalar 取回自增主键。"},
                {"kind": "code", "text": "INSERT INTO People (Name, Age) VALUES ($name, $age);\nSELECT last_insert_rowid();"},
            ],
        },
    ],
}


def build_sections(doc):
    """把 {stem,title,source_url,sections:[...]} 展开成 docref 的 section 列表。"""
    stem = doc["stem"]
    out = []
    for i, sec in enumerate(doc["sections"], start=1):
        url = sec.get("source_url", doc["source_url"])
        blocks = [{"kind": "text", "page": i, "ord": 0, "top": 800.0,
                   "text": f"来源（联网同步，非 PDF 页码）: {url}"}]
        for j, blk in enumerate(sec["blocks"], start=1):
            blocks.append({
                "kind": blk["kind"],
                "page": i,
                "ord": j,
                "top": round(800.0 - j * 25.0, 2),
                "text": blk["text"],
            })
        out.append({
            "id": f"{stem}#{i:04d}",
            "path": ["Microsoft.Data.Sqlite", sec["title"]],
            "title": sec["title"],
            "level": 1,
            "pages": [i],
            "codeRefs": [],
            "blocks": blocks,
        })
    return out


def write_doc(kb_dir, doc):
    stem = doc["stem"]
    sections = build_sections(doc)
    total_blocks = sum(len(s["blocks"]) for s in sections)

    manifest = {
        "source": {
            "file": doc["source_url"],
            "sha256": "",
            "title": doc["title"],
            "producer": "web-sync",
            "pages": len(sections),
            "extractedAt": datetime.now(timezone.utc).isoformat(),
            "extractorVersion": "web-sync-0.1.0",
        },
        "warnings": [
            "此文档由联网搜索同步而来，非官方 PDF 提取；pages 为合成的虚拟页码，"
            "引用时请用 [联网-官方:]/[联网-非官方:] 标注，不要用 [来源: id p页码]。"
        ],
        "sectionCount": len(sections),
        "blockCount": total_blocks,
    }

    sections_path = f"{kb_dir}/{stem}.sections.jsonl"
    manifest_path = f"{kb_dir}/{stem}.manifest.json"

    with open(sections_path, "w", encoding="utf-8") as f:
        for sec in sections:
            f.write(json.dumps(sec, ensure_ascii=False) + "\n")

    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)

    print(f"已写入 {sections_path}（{len(sections)} 个 section，{total_blocks} 个 block）")
    print(f"已写入 {manifest_path}")


def main(argv):
    if len(argv) > 1:
        with open(argv[1], encoding="utf-8") as f:
            doc = json.load(f)
    else:
        doc = DEMO_DOC
    write_doc(KB_DIR, doc)


if __name__ == "__main__":
    main(sys.argv)
