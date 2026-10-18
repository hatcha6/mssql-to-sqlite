using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace MssqlToSqlite.Ui
{
    /// <summary>
    /// Preview harness (the WinForms cousin of Pointy's dev preview entrypoints):
    /// <c>--screenshot out.png --demo export</c> opens the window in a fake state,
    /// captures it from the screen and exits. CI uploads these so the UI can be
    /// reviewed without a Windows machine.
    /// </summary>
    internal static class Screenshot
    {
        public static void Attach(MainForm form, string path, string demo)
        {
            form.Shown += (s, e) =>
            {
                form.ApplyDemo(demo ?? "idle");
                form.Activate();
                var timer = new Timer { Interval = 1500 };
                timer.Tick += (s2, e2) =>
                {
                    timer.Stop();
                    try
                    {
                        Rectangle bounds = form.Bounds;
                        using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                        {
                            using (Graphics g = Graphics.FromImage(bitmap))
                                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                            bitmap.Save(path, ImageFormat.Png);
                        }
                    }
                    catch (Exception error)
                    {
                        Console.Error.WriteLine(error);
                    }
                    form.ForceClose();
                };
                timer.Start();
            };
        }
    }
}
