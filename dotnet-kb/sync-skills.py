# -*- coding: utf-8 -*-
"""把 SKILL.md 与 sync-web-kb.py 同步到各智能体的加载位置，并把机器相关的路径填进去。

正文只有两份（SKILL.md + sync-web-kb.py），各处只差 frontmatter 与路径 —— 这样不会出现「改了一边忘了另一边」。

正文里有三个占位符，由本脚本在写出时替换：

    @DOCREF@    docref.exe 的绝对路径
    @KB@        知识库目录的绝对路径（默认仓库内 kb/）
    @SYNCWEB@   sync-web-kb.py 的绝对路径（与 SKILL.md 同目录）

**为什么用占位符而不是把路径写死在正文里**：正文要能推到别的机器上，而这两个路径
每台机器都不一样（用户名、盘符、知识库放哪）。写死一次，换机器就得手工改十几处，
漏一处就是一条静默失效的命令 —— 而失效的样子是「查不到所以没查」，没人会发现。

**为什么输出正斜杠**：这些命令最终在 Git Bash 里跑，`C:\\Users\\...` 的反斜杠会被当转义符
吃掉，变成 `C:Users...`。正斜杠在 Git Bash 和 Windows API 下都成立，实测确认。

为什么是 Python 而不是 PowerShell：cove 的 frontmatter 解析要求文件以 `---\\n` 开头
（internal/skills/skills.go 的 parseFrontmatter），**BOM 或 CRLF 会让整段 frontmatter
被静默忽略**，症状是 skill 照样加载、但 paths 失效、再也不自动触发。PowerShell 5.1 的
Set-Content / Out-File 默认写 ANSI 或带 BOM 的 UTF-8，正好踩这个坑。这里用
io.open(..., encoding="utf-8", newline="\\n") 明确控制，并在写完后回读校验。

用法：python dotnet-kb/sync-skills.py [--cove]
"""
import glob
import io
import os
import sys

# 输出被管道接走时，Python 用的是本地代码页（Windows 上 cp936），中文诊断信息会变成乱码。
# 下面的检查失败信息是给人看的，乱码等于没写，所以写死 UTF-8。
# **stderr 也要改** —— sys.exit() 的报错走 stderr，只改 stdout 的话，
# 最该被看懂的那几条恰恰是乱码的（踩过）。
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "SKILL.md")
SYNC_WEB_SRC = os.path.join(HERE, "sync-web-kb.py")
HOME = os.path.expanduser("~")

# ---------------------------------------------------------------------------
# 换机器只改这里（或改用同名环境变量，不改文件）。
# ---------------------------------------------------------------------------

# docref.exe 必须是**整个 publish 目录里的那个**，不能只拷 exe：
# 单个 exe 跑不起来，它只是 apphost，会去找同目录的 docref.dll。
# 只拷 exe 的症状：The application to execute does not exist: '...\docref.dll'
# （实测过，见 README。）
DOCREF_EXE = os.environ.get("DOCREF_EXE") or os.path.join(
    HOME, ".claude", "dist", "docref", "docref.exe")

# 知识库目录，里面放 *.sections.jsonl + *.manifest.json 成对的文件。
# 默认指向仓库内的 kb/（相对脚本位置算），clone 到哪台机器都成立；
# 想用自己的位置可设环境变量 DOCREF_KB 覆盖。
KB_DIR = os.environ.get("DOCREF_KB") or os.path.join(HERE, "..", "kb")

# ---------------------------------------------------------------------------

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


def targets():
    """(目标路径, 用哪份 frontmatter；None = 原样照抄)。

    默认只写给 Claude Code —— cove 那份要用 --cove 显式打开，
    免得在没有 cove 的机器上凭空造出 ~/.cove/plugins/docref/ 目录。
    """
    out = [(os.path.join(HOME, ".claude", "skills", "dotnet-kb", "SKILL.md"), None)]
    if "--cove" in sys.argv:
        # cove 那份和 docref.exe 住在同一个插件目录里，整体可拷走。
        # cove 会扫 ~/.cove/plugins/<dir>/skills（skills.go LoadAll 第 384 行）。
        out.append((os.path.join(HOME, ".cove", "plugins", "docref", "skills",
                                 "dotnet-kb-cove", "SKILL.md"), COVE_FRONTMATTER))
    return out


def to_slash(path):
    """绝对化并转正斜杠 —— 命令在 Git Bash 里跑，反斜杠会被当转义符吃掉。"""
    return os.path.abspath(path).replace("\\", "/")


def check_prerequisites():
    """同步前把两个前提查实。不猜 —— 缺了就在这里失败，别等命令静默跑空。"""
    problems = []

    if not os.path.isfile(DOCREF_EXE):
        problems.append("找不到 docref.exe：%s" % DOCREF_EXE)
    elif not os.path.isfile(os.path.join(os.path.dirname(DOCREF_EXE), "docref.dll")):
        problems.append("docref.exe 同目录没有 docref.dll —— 只拷了 exe，这个程序跑不起来："
                        "\n      %s\n      要拷整个 publish 目录（exe + docref.dll + deps.json "
                        "+ runtimeconfig.json + PdfPig 的几个 DLL）。" % os.path.dirname(DOCREF_EXE))

    if not os.path.isdir(KB_DIR):
        problems.append("找不到知识库目录：%s" % KB_DIR)
    elif not glob.glob(os.path.join(KB_DIR, "*.sections.jsonl")):
        problems.append("知识库目录里没有 *.sections.jsonl（空目录或指错了）：%s" % KB_DIR)

    if problems:
        sys.exit("同步前检查没过，什么都没写：\n  - " + "\n  - ".join(problems))


def split_frontmatter(text):
    """返回 (frontmatter 含首尾 ---, body)。格式不对就直接失败，不猜。"""
    if not text.startswith("---\n"):
        sys.exit("源文件不以 ---\\n 开头，cove 会忽略整段 frontmatter")
    end = text.index("\n---\n", 4)
    return text[: end + 5], text[end + 5 :]


def render(body, syncweb_path):
    """把占位符填成实际路径，填完必须一个不剩。"""
    out = (body
           .replace("@DOCREF@", to_slash(DOCREF_EXE))
           .replace("@KB@", to_slash(KB_DIR))
           .replace("@SYNCWEB@", to_slash(syncweb_path)))
    leftover = [p for p in ("@DOCREF@", "@KB@", "@SYNCWEB@") if p in out]
    if leftover:
        sys.exit("占位符没替换干净：%s（正文里是不是有拼错的？）" % ", ".join(leftover))
    return out


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


def write_py(path, text):
    """写 sync-web-kb.py：UTF-8 / LF / 无 BOM（不校验 --- 前缀，那是 SKILL.md 的事）。"""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with io.open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
    with io.open(path, "rb") as f:
        raw = f.read()
    assert not raw.startswith(b"\xef\xbb\xbf"), "%s 写出了 BOM" % path
    assert b"\r" not in raw, "%s 写出了 CRLF" % path
    return len(raw)


def main():
    check_prerequisites()
    # utf-8-sig：有 BOM 就吃掉（被 PowerShell 编辑过就会带上），没有则等同 utf-8。
    # newline="" 保留原始换行，下面再统一归一化。
    with io.open(SRC, "r", encoding="utf-8-sig", newline="") as f:
        src = f.read()
    # 源文件可能是 CRLF —— core.autocrlf=true 的机器上 git checkout 会转，
    # 或者被别的编辑器改过。归一化成 LF：对 markdown 来说无损，而 CRLF 会让 cove
    # 的 frontmatter 静默失效，也可能让 write_checked 在最后一步才炸。
    src = src.replace("\r\n", "\n").replace("\r", "\n")
    frontmatter, body = split_frontmatter(src)

    # sync-web-kb.py 是纯 Python，占位符只有 @KB@；同样归一化 + 渲染。
    with io.open(SYNC_WEB_SRC, "r", encoding="utf-8-sig", newline="") as f:
        sync_web_src = f.read()
    sync_web_src = sync_web_src.replace("\r\n", "\n").replace("\r", "\n")

    print("填入的路径：")
    print("  docref  %s" % to_slash(DOCREF_EXE))
    print("  知识库  %s" % to_slash(KB_DIR))
    print("写出：")

    for path, fm in targets():
        syncweb_path = os.path.join(os.path.dirname(path), "sync-web-kb.py")
        # 占位符只出现在 body 里，frontmatter 原样带过（Claude Code 用源文件那份）。
        size = write_checked(path, (fm if fm is not None else frontmatter) + render(body, syncweb_path))
        print("  %-58s %6d bytes" % (path, size))
        size2 = write_py(syncweb_path, render(sync_web_src, syncweb_path))
        print("  %-58s %6d bytes" % (syncweb_path, size2))
    print("同步完成，正文来自 %s 与 %s" % (SRC, SYNC_WEB_SRC))


if __name__ == "__main__":
    main()
