using System.Text;

namespace Docref;

/// <summary>把一页的 <see cref="TextLine"/> 归类并合并成 <see cref="Block"/>。</summary>
public static class BlockClassifier
{
    /// <summary>
    /// 视为段落分隔的垂直间距，按字号比例。
    ///
    /// 必须大于正常行距（12pt 正文实测 19.5pt = 1.63 倍），又必须小于段间距
    /// （实测 31.5pt = 2.63 倍）。之所以按比例而不是固定值：标题字号大（中文文档实测 20.1pt，
    /// 行距约 26pt），固定 25pt 会把折行的标题切成两块。
    ///
    /// 用当前行的字号而不是上一行的 —— 从大字号标题过渡到正文时，
    /// 用标题字号算出的阈值太宽，会把标题和正文粘成一块。
    /// 只对正文生效：代码里的空行不是段落分隔。
    /// </summary>
    private const double ParagraphGapRatio = 2.0;

    /// <summary>
    /// 代码块内视为空行的行距倍数（相对字号）。实测代码字号 10.5pt、行距 14.25pt（1.36 倍），
    /// 空行处 28.5pt（2.71 倍），阈值取中间。
    /// </summary>
    private const double BlankLineRatio = 1.8;

    /// <param name="codeMargin">
    /// 代码区的右边距（PDF 坐标）。由调用方按页面真实几何算出（页宽 − 代码左缘）。
    ///
    /// 刻意不在这里按观测值求 —— 若某页只有几行短代码，观测到的最大右缘会远小于真实边距，
    /// 于是相邻两行会被误判成折行粘在一起。默认 <see cref="double.MaxValue"/> 表示
    /// 「边距未知 → 一律不接回」，这是安全的默认：漏接一个折行只是难读，
    /// 错接两行则产出坏代码。
    /// </param>
    public static IReadOnlyList<Block> Classify(
        int page, IReadOnlyList<TextLine> lines, double codeMargin = double.MaxValue)
    {
        var blocks = new List<Block>();
        var buffer = new List<TextLine>();
        string? bufferKind = null;
        var previousY = double.NaN;

        foreach (var line in lines)
        {
            var kind = KindOf(line);

            // PDF 坐标 Y 向上，所以上一行的 Y 更大。
            var gap = double.IsNaN(previousY) ? 0 : previousY - line.Y;

            var size = line.Runs.Count > 0 ? line.Runs[0].Size : 0;

            var breakHere = bufferKind is not null
                            && (kind != bufferKind
                                || (kind == Block.KindText && gap > size * ParagraphGapRatio));

            if (breakHere)
            {
                blocks.Add(Emit(page, blocks.Count, bufferKind!, buffer, codeMargin));
                buffer = [];
            }

            bufferKind = kind;
            previousY = line.Y;
            buffer.Add(line);
        }

        if (buffer.Count > 0)
        {
            blocks.Add(Emit(page, blocks.Count, bufferKind!, buffer, codeMargin));
        }

        return blocks;
    }

    /// <summary>
    /// 整行都是等宽字体才算代码块。混排行里的等宽 run 是行内代码，由
    /// <see cref="InlineMarkdown"/> 渲染成反引号，不算代码块。
    /// </summary>
    private static string KindOf(TextLine line)
        => line.Runs.All(r => FontFacts.IsMonospace(r.Font))
            ? Block.KindCode
            : Block.KindText;

    /// <summary>
    /// Top 取最靠上那行的基线；输入已是阅读顺序，所以就是第一行。
    /// 取两位小数 —— 原始值带浮点噪声（795.3799994511668），对「劈开边界页」这个用途没意义。
    /// </summary>
    private static Block Emit(int page, int ord, string kind, List<TextLine> lines, double codeMargin)
    {
        var top = Math.Round(lines[0].Y, 2);

        return kind == Block.KindCode
            ? new Block(kind, page, ord, top, Text: JoinCode(lines, codeMargin))
            : new Block(kind, page, ord, top, Text: JoinProse(lines));
    }

    /// <summary>
    /// 正文的折行要合回一段 —— PDF 里的换行只是排版折行，保留成 \n 会让下游误判为硬换行。
    ///
    /// 只有折行两侧都是中文时才不补空格。三种情况实测各有依据：
    /// 中文↔中文 "已启" + "用。" 不能补（会成 "已启 用。"）；
    /// 中文↔拉丁 "…启用" + "ValidateOnBuild…" 要补（PDF 大纲里的标题证实带空格）；
    /// 拉丁↔拉丁 必须补，否则单词粘连。
    /// </summary>
    private static string JoinProse(List<TextLine> lines)
    {
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var text = InlineMarkdown.Render(line);
            if (text.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0 && !(TextFacts.IsCjk(builder[^1]) && TextFacts.IsCjk(text[0])))
            {
                builder.Append(' ');
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 代码用换行连接（换行是代码的一部分），并在约两倍行距处还原空行 ——
    /// 空行分隔 using 段与类型声明，去掉就改变了代码的样子。
    /// </summary>
    /// <summary>比例检查所需的最少断行数。少于这个数没有统计意义，直接信几何。</summary>
    private const int MinBreaksForRatioCheck = 5;

    /// <summary>
    /// 行注释标记。以它们开头的行绝不是排版折行的续行 —— 折行不会在断点插入注释标记。
    /// 发现其他语言的标记时往这里加。
    /// </summary>
    private static readonly string[] CommentMarkers = ["//", "/*", "#"];

    /// <summary>
    /// 一个块里被判为折行的断行占比上限。
    /// 实测代码块 p760：30 行 5 处折行 = 17%；而 Northwind CSV 数据样本 100% 满足。
    /// 阈值取中间。
    /// </summary>
    private const double MaxWrapRatio = 0.4;

    private static string JoinCode(List<TextLine> lines, double codeMargin)
    {
        var rejoinWraps = ShouldRejoinWraps(lines, codeMargin);
        var builder = new StringBuilder(lines[0].Text);

        for (var i = 1; i < lines.Count; i++)
        {
            var previous = lines[i - 1];
            var current = lines[i];

            // 被 PDF 排版折下来的续行：直接接回上一行，不插换行。
            // 折行处的空格属于上一行，所以也不补空格。
            if (rejoinWraps && IsWrapContinuation(previous, current, codeMargin))
            {
                builder.Append(current.Text);
                continue;
            }

            var gap = previous.Y - current.Y;
            var size = current.Runs.Count > 0 ? current.Runs[0].Size : 0;

            builder.Append(gap > size * BlankLineRatio ? "\n\n" : "\n");
            builder.Append(current.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 几何推断是否适用于这个块。
    ///
    /// 折行在代码块里是少数（实测 dotnet-csharp.pdf p760：30 行 5 处折行 = 17%）。
    /// 若几乎每一处断行都满足折行条件，说明这个块不是折行的代码，而是**每行都接近满宽的
    /// 数据或控制台输出** —— 实测 Northwind CSV 样本 100% 满足，28 条完整记录会被粘成
    /// 一行 3184 字符。此时整块都不接回。
    ///
    /// 几何本身区分不了「恰好接近满行的完整行」和「真折行」：CSV 那页上一行右缘 475.8、
    /// 边距 518.4，任何后续行都"放不下"。所以判据只能上升到块层面。
    /// </summary>
    private static bool ShouldRejoinWraps(List<TextLine> lines, double codeMargin)
    {
        var breaks = lines.Count - 1;
        if (breaks < MinBreaksForRatioCheck)
        {
            return true;
        }

        var looksWrapped = 0;
        for (var i = 1; i < lines.Count; i++)
        {
            if (IsWrapContinuation(lines[i - 1], lines[i], codeMargin))
            {
                looksWrapped++;
            }
        }

        return looksWrapped <= breaks * MaxWrapRatio;
    }

    /// <summary>
    /// 本行是不是上一行被 PDF 排版折下来的续行。**贪心换行的逆运算**：
    /// 本行内容若接在上一行后面会超出右边距，那它就是被折下来的。
    ///
    /// 判据来自 dotnet-csharp.pdf 第 760 页实测。两个曾被我误判的直觉：
    /// 「续行顶到容器左缘」不成立 —— 全部代码行 X 都是 77.5，缩进在文本里是真空格；
    /// 「上一行顶到右边距」也不成立 —— <c>var client = new </c> 右缘只有 296.9（边距 498.9），
    /// 它折行是因为下一段太宽放不下。
    /// </summary>
    private static bool IsWrapContinuation(TextLine previous, TextLine current, double codeMargin)
    {
        // 缩进在文本里是真空格；排版折出来的续行没有缩进。
        if (current.Text.Length == 0 || char.IsWhiteSpace(current.Text[0]))
        {
            return false;
        }

        // 续行不可能以注释标记开头 —— 排版折行不会在断点插入 // 或 #。
        // 实测：连续的注释行曾被粘成 "…like the following://     Task #0 created at…"。
        if (CommentMarkers.Any(m => current.Text.StartsWith(m, StringComparison.Ordinal)))
        {
            return false;
        }

        // 排版不会在语句结束处折行。没有这条护栏，恰好顶到右边距的语句后面跟一个
        // 无缩进的 } 就会被粘上去，产出坏代码。
        var tail = previous.Text.TrimEnd();
        if (tail.Length > 0 && tail[^1] is ';' or '{' or '}')
        {
            return false;
        }

        return previous.Right + (current.Right - current.X) > codeMargin;
    }
}
