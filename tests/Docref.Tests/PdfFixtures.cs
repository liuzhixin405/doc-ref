namespace Docref.Tests;

/// <summary>
/// 真实 PDF 样本的位置。可用环境变量 <c>DOCREF_PDF_FIXTURES</c> 覆盖。
/// 样本缺失时**大声失败**而不是静默跳过 —— 静默跳过的集成测试等于没有。
/// </summary>
internal static class PdfFixtures
{
    private static string Root =>
        Environment.GetEnvironmentVariable("DOCREF_PDF_FIXTURES") ?? @"D:\pdf";

    /// <summary>C# 语言参考，1909 页，Microsoft Learn 导出。</summary>
    public const string CSharpLanguageReference = "dotnet-csharp-language-reference.pdf";

    /// <summary>ASP.NET Core 中文文档，7780 页 —— 用于验证中文标题与大体量。</summary>
    public const string AspNetCoreChinese = "aspnet-core-aspnetcore-11.0.pdf";

    /// <summary>2001 年 Acrobat Distiller 出的纯扫描件，无文本层 —— 用于验证拒绝路径。</summary>
    public const string ScannedBook = "C#设计模式_中文版.pdf";

    public static string Path(string fileName)
    {
        var full = System.IO.Path.Combine(Root, fileName);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException(
                $"找不到测试样本 PDF: {full}\n" +
                $"请把官方文档 PDF 放到 {Root}，或设置环境变量 DOCREF_PDF_FIXTURES 指向它们所在目录。");
        }

        return full;
    }
}
