using System.Text;

namespace Docref;

/// <summary>把一行的 <see cref="TextRun"/> 渲染成带行内格式的 Markdown 文本。</summary>
public static class InlineMarkdown
{
    /// <summary>不应保留前导空格的标点（收尾类）。开括号类不在此列，"foo (bar)" 的空格是对的。</summary>
    private const string TrailingPunctuation = ".,;:!?)]}。，、；：！？）】";

    /// <summary>
    /// 补分隔空格的间隙门槛，按字号比例 —— 一个整字宽。
    ///
    /// 刻意比 <see cref="GlyphMerger.KerningTolerance"/>（0.3）大得多：切分 run 保守一点无害，
    /// 补空格保守是必须的。中文两端对齐会在字间塞几个 pt 的间距，用 0.3 会把 "已启用"
    /// 拆成 "已启 用"；而表格列间隙有上百 pt，一个整字宽足以区分两者。
    /// </summary>
    private const double SeparatorGapRatio = 1.0;

    public static string Render(TextLine line)
    {
        var builder = new StringBuilder();
        TextRun? previous = null;

        foreach (var run in line.Runs)
        {
            // run 之间的间隙宽到一个整字，才补分隔空格 —— 不补的话表格两列会拼成
            // "boolSystem.Boolean"；门槛太低又会把两端对齐的中文拆开。
            if (previous is not null
                && run.X - (previous.X + previous.Width) > previous.Size * SeparatorGapRatio)
            {
                builder.Append(' ');
            }

            builder.Append(FontFacts.IsMonospace(run.Font) ? $"`{run.Text}`" : run.Text);
            previous = run;
        }

        return Normalize(builder.ToString());
    }

    /// <summary>
    /// 清理 Learn 的 PDF 在行内代码两侧塞的填充空格：折叠连续空格，并去掉收尾标点前的空格。
    /// </summary>
    private static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (ch == ' ' && sb.Length > 0 && sb[^1] == ' ')
            {
                continue;
            }

            if (TrailingPunctuation.Contains(ch) && sb.Length > 0 && sb[^1] == ' ')
            {
                sb.Length--;
            }

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }
}
