# -*- coding: utf-8 -*-
"""把 SKILL.md 同步到两个智能体各自的加载位置。

正文只有 SKILL.md 一份，两个目标只差 frontmatter —— 这样不会出现「改了一边忘了另一边」。

为什么是 Python 而不是 PowerShell：cove 的 frontmatter 解析要求文件以 `---\n` 开头
（internal/skills/skills.go 的 parseFrontmatter），**BOM 或 CRLF 会让整段 frontmatter
被静默忽略**，症状是 skill 照样加载、但 paths 失效、再也不自动触发。PowerShell 5.1 的
Set-Content / Out-File 默认写 ANSI 或带 BOM 的 UTF-8，正好踩这个坑。这里用
io.open(..., encoding="utf-8", newline="\n") 明确控制，并在写完后回读校验。

用法：python dotnet-kb/sync-skills.py
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "SKILL.md")
HOME = os.path.expanduser("~")

# cove 专用 frontmatter。三处 cove 特有的约束，改之前先读懂：
#
# 1. 名字必须和 ~/.claude/skills 里那份不同。cove 的 LoadAll 先加载 ~/.cove/skills、
#    后加载 ~/.claude/skills，按 name 注册进同一个 map —— 同名的话后者覆盖前者，
#    放在 ~/.cove/skills/dotnet-kb/ 的那份永远不会生效。实测确认。
#
# 2. paths 的值 cove 不剥引号（parseFrontmatter 直接按 "," 切分原始字符串），
#    首尾两个 glob 会变成 "*.cs 和 *.slnx" 而匹配不上任何文件。
#    用 __cove_quote_guard 占住首尾这两个被污染的位置。
#    （cove 自带的 12 个 skill 都有这个问题，根治要改 Go：v = strings.Trim(v, "\"'")）
#
# 3. steps 也按 "," 切分，所以每一步内部不能出现半角逗号，只能用顿号。
COVE_FRONTMATTER = """---
name: dotnet-kb-cove
description: 写或改任何 C#/.NET 代码之前先查本地官方文档知识库；未命中按 L1 官方域 → L2 放开 → L3 未核实逐级降级，每处标注来源级别。
paths: "__cove_quote_guard,*.cs,*.csproj,*.razor,*.cshtml,*.sln,*.slnx,__cove_quote_guard"
steps: 列出这次会用到的 API 与配置项,逐个查本地知识库,未命中的按 L1→L2→L3 逐级降级,按查到的写法写代码并逐处标注来源级别,把 L2 与 L3 的部分单独列出来交人复审
---
"""

TARGETS = [
    # (目标路径, 用哪份 frontmatter；None = 原样照抄)
    (os.path.join(HOME, ".claude", "skills", "dotnet-kb", "SKILL.md"), None),
    # cove 那份和 docref.exe 住在同一个插件目录里，整体可拷走。
    # cove 会扫 ~/.cove/plugins/<dir>/skills（skills.go LoadAll 第 384 行）。
    (os.path.join(HOME, ".cove", "plugins", "docref", "skills",
                  "dotnet-kb-cove", "SKILL.md"), COVE_FRONTMATTER),
]


def split_frontmatter(text):
    """返回 (frontmatter 含首尾 ---, body)。格式不对就直接失败，不猜。"""
    if not text.startswith("---\n"):
        sys.exit("源文件不以 ---\n 开头，cove 会忽略整段 frontmatter")
    end = text.index("\n---\n", 4)
    return text[: end + 5], text[end + 5 :]


def write_checked(path, text):
    """写 UTF-8 / LF / 无 BOM，然后回读校验 —— 这三条错一个 frontmatter 就静默失效。"""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    with io.open(path, "rb") as f:
        raw = f.read()
    assert not raw.startswith(b"\xef\xbb\xbf"), "%s 写出了 BOM" % path
    assert b"\r" not in raw, "%s 写出了 CRLF" % path
    assert raw.startswith(b"---\n"), "%s 不以 ---\n 开头" % path
    return len(raw)


def main():
    with io.open(SRC, "r", encoding="utf-8", newline="") as f:
        src = f.read()
    _, body = split_frontmatter(src)

    for path, frontmatter in TARGETS:
        text = src if frontmatter is None else frontmatter + body
        size = write_checked(path, text)
        print("  %-58s %6d bytes" % (path, size))
    print("同步完成，正文来自 %s" % SRC)


if __name__ == "__main__":
    main()
