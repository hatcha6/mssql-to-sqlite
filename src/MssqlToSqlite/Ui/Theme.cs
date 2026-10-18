using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MssqlToSqlite.Ui
{
    /// <summary>
    /// Pointy's design tokens (frontend/lib/src/shared/design/pointy_colors.dart,
    /// pointy_component_styles.dart, pointy_typography.dart) carried over to
    /// WinForms, so this tool reads as part of دفتر.
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Primary = Hex(0x0F766E);
        public static readonly Color PrimaryStrong = Hex(0x006C53);
        public static readonly Color PrimaryDark = Hex(0x064E3B);
        public static readonly Color DarkTopBar = Hex(0x0B111C);
        public static readonly Color AccentAmber = Hex(0xC98A3B);
        public static readonly Color Danger = Hex(0xB42318);
        public static readonly Color Warning = Hex(0xB65F2A);
        public static readonly Color Success = Hex(0x0E6B4E);
        public static readonly Color Ink = Hex(0x101828);
        public static readonly Color MutedInk = Hex(0x667085);
        public static readonly Color Line = Hex(0xE5E0D8);
        public static readonly Color LineStrong = Hex(0xD5CFC4);
        public static readonly Color Surface = Hex(0xFFFFFF);
        public static readonly Color Page = Hex(0xF8F7F4);
        public static readonly Color SubtleFill = Hex(0xF2F4F2);
        public static readonly Color SurfaceSunken = Hex(0xF1EFEA);
        public static readonly Color PrimaryContainer = Hex(0xE0F2EF);
        public static readonly Color AmberContainer = Hex(0xFFF4E3);
        public static readonly Color DangerContainer = Hex(0xFEF3F2);

        // PointyRadii
        public const int RadiusCard = 8;
        public const int RadiusInput = 12;
        public const int RadiusButton = 12;
        public const int RadiusChip = 10;

        private static float _scale = 1f;
        private static readonly PrivateFontCollection Fonts = new PrivateFontCollection();
        private static readonly Dictionary<string, FontFamily> Families = new Dictionary<string, FontFamily>();

        /// <summary>Short screens (1024x768, 1366x768): tighter fields, like Pointy's compact till layout.</summary>
        public static bool Compact { get; private set; }

        /// <summary>Height of inputs, dropdowns, buttons and the segmented control.</summary>
        public static int FieldHeight => S(Compact ? 34 : 40);

        /// <summary>Device pixels for a 96-dpi design value.</summary>
        public static int S(int value) => (int)Math.Round(value * _scale);

        public static void Initialize()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
                _scale = g.DpiX / 96f;
            Compact = Screen.PrimaryScreen.WorkingArea.Height < S(900);
            LoadFont("Regular");
            LoadFont("SemiBold");
            LoadFont("Bold");
        }

        public static Font Body => Make("Regular", 10f, FontStyle.Regular);
        public static Font BodyStrong => Make("SemiBold", 10f, FontStyle.Regular);
        public static Font Small => Make("Regular", 9f, FontStyle.Regular);
        public static Font Label => Make("SemiBold", 9f, FontStyle.Regular);
        public static Font Title => Make("SemiBold", 12f, FontStyle.Regular);
        public static Font Brand => Make("Bold", 14f, FontStyle.Regular);

        private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

        private static Font Make(string weight, float size, FontStyle style)
        {
            string key = weight + size;
            if (FontCache.TryGetValue(key, out Font font))
                return font;

            // Static Plex files: SemiBold is its own legacy family ("… SemiBold"),
            // Bold is the Bold style of the regular family.
            FontFamily family;
            if (weight == "SemiBold" && Families.TryGetValue("SemiBold", out family))
                font = new Font(family, size, style, GraphicsUnit.Point);
            else if (Families.TryGetValue("Regular", out family))
                font = new Font(family, size, weight == "Regular" ? style : style | FontStyle.Bold, GraphicsUnit.Point);
            else
                font = new Font("Segoe UI", size, weight == "Regular" ? style : style | FontStyle.Bold, GraphicsUnit.Point);
            FontCache[key] = font;
            return font;
        }

        private static void LoadFont(string weight)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("font.IBMPlexSansArabic-" + weight + ".ttf"))
                {
                    if (stream == null)
                        return;
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);

                    // The collection needs the memory for its lifetime (the process); never freed.
                    IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, memory, bytes.Length);
                    Fonts.AddMemoryFont(memory, bytes.Length);
                    // Also register with GDI so native TextBox/ComboBox/ListView render it.
                    uint installed = 0;
                    AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, ref installed);
                }

                foreach (FontFamily family in Fonts.Families)
                {
                    bool semi = family.Name.IndexOf("SemiBold", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (semi)
                        Families["SemiBold"] = family;
                    else if (family.Name.IndexOf("Plex", StringComparison.OrdinalIgnoreCase) >= 0)
                        Families["Regular"] = family;
                }
            }
            catch (Exception)
            {
                // Segoe UI fallback; Arabic still shapes correctly.
            }
        }

        public static Image Logo()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png"))
                return stream == null ? null : Image.FromStream(stream);
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Rectangle bounds, int radius, Color fill, Color? border = null)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedRect(bounds, radius))
            {
                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
                if (border.HasValue)
                {
                    using (var pen = new Pen(border.Value, 1f))
                        g.DrawPath(pen, path);
                }
            }
        }

        public static TextFormatFlags TextFlags(Control control, TextFormatFlags flags)
        {
            flags |= TextFormatFlags.NoPrefix;
            if (control.RightToLeft == RightToLeft.Yes)
                flags |= TextFormatFlags.RightToLeft;
            return flags;
        }

        private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);
    }
}
