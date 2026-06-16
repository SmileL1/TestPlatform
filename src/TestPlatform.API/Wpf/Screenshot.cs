using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace TestPlatform.API.Wpf;

/// <summary>截图（记录测试过程）</summary>
public static class Screenshot
{
    public static string CaptureFullScreen(string testName, int stepNumber)
    {
        var dir = Path.Combine("TestResults", testName, "screenshots");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"step_{stepNumber:D3}_{DateTime.Now:HHmmss}.png");

        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>
    /// 截取指定屏幕区域并返回 PNG 的 base64（用于发给 AI 视觉模型）。
    /// 区域无效时回退截主屏。
    /// </summary>
    public static string CaptureRegionBase64(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            x = b.X; y = b.Y; width = b.Width; height = b.Height;
        }
        using var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(new Point(x, y), Point.Empty, new Size(width, height));
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }
}
