using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MssqlToSqlite.Ui
{
    // Pointy's shared components, in miniature. Layout is RTL through
    // TableLayoutPanel/RightToLeft rather than Form.RightToLeftLayout, so these
    // paint in plain coordinates and mirror themselves where order matters.

    /// <summary>White surface, 8px radius, hairline border — Pointy's card.</summary>
    internal sealed class Card : Panel
    {
        public Card()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Page;
            Padding = new Padding(Theme.S(16));
        }

        /// <summary>Surface by default; SurfaceSunken for wells such as the log.</summary>
        public Color Fill { get; set; } = Theme.Surface;

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(e.Graphics, bounds, Theme.S(Theme.RadiusCard), Fill, Theme.Line);
        }
    }

    internal enum ButtonVariant
    {
        Filled,
        Outlined,
        Text,
    }

    internal sealed class PointyButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public PointyButton(string text, ButtonVariant variant = ButtonVariant.Outlined)
        {
            Text = text;
            Variant = variant;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.BodyStrong;
            Height = Theme.S(40);
            Cursor = Cursors.Hand;
            Margin = new Padding(Theme.S(4));
        }

        public ButtonVariant Variant { get; set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font);
            return new Size(text.Width + Theme.S(36), Theme.S(40));
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = Theme.S(Theme.RadiusButton);
            Color text;

            switch (Variant)
            {
                case ButtonVariant.Filled:
                    Color fill = !Enabled ? Theme.LineStrong : _pressed ? Theme.PrimaryDark : _hover ? Theme.PrimaryStrong : Theme.Primary;
                    Theme.FillRounded(g, bounds, radius, fill);
                    text = Enabled ? Color.White : Theme.Surface;
                    break;
                case ButtonVariant.Outlined:
                    Color back = _pressed ? Theme.PrimaryContainer : _hover ? Theme.SubtleFill : Theme.Surface;
                    Theme.FillRounded(g, bounds, radius, back, Enabled ? Theme.LineStrong : Theme.Line);
                    text = Enabled ? Theme.PrimaryStrong : Theme.MutedInk;
                    break;
                default:
                    if (_hover || _pressed)
                        Theme.FillRounded(g, bounds, radius, _pressed ? Theme.PrimaryContainer : Theme.SubtleFill);
                    text = Enabled ? Theme.PrimaryStrong : Theme.MutedInk;
                    break;
            }

            if (Focused && ShowFocusCues)
            {
                var focus = Rectangle.Inflate(bounds, -Theme.S(3), -Theme.S(3));
                using (GraphicsPath path = Theme.RoundedRect(focus, radius - Theme.S(3)))
                using (var pen = new Pen(Variant == ButtonVariant.Filled ? Color.White : Theme.Primary, 1f) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, Text, Font, bounds, text,
                Theme.TextFlags(this, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
        }
    }

    /// <summary>A borderless TextBox inside a rounded 12px field, with a teal focus ring.</summary>
    internal sealed class InputShell : Panel
    {
        private const int EM_SETCUEBANNER = 0x1501;

        public InputShell(string placeholder = null, bool password = false)
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Surface;
            Height = Theme.S(40);
            Margin = new Padding(Theme.S(4));
            Padding = new Padding(Theme.S(12), Theme.S(10), Theme.S(12), Theme.S(6));

            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Font = Theme.Body,
                BackColor = Theme.Surface,
                ForeColor = Theme.Ink,
                UseSystemPasswordChar = password,
            };
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.EnabledChanged += (s, e) => { BackColor = Box.Enabled ? Theme.Surface : Theme.SubtleFill; Box.BackColor = BackColor; Invalidate(); };
            if (placeholder != null)
                Box.HandleCreated += (s, e) => SendMessage(Box.Handle, EM_SETCUEBANNER, (IntPtr)1, placeholder);
            Controls.Add(Box);
            Click += (s, e) => Box.Focus();
        }

        public TextBox Box { get; }

        public override string Text
        {
            get => Box.Text;
            set => Box.Text = value;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Surface);
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            bool focused = Box.Focused;
            Theme.FillRounded(e.Graphics, bounds, Theme.S(Theme.RadiusInput), BackColor, focused ? Theme.Primary : Theme.LineStrong);
            if (focused)
            {
                using (GraphicsPath path = Theme.RoundedRect(Rectangle.Inflate(bounds, -1, -1), Theme.S(Theme.RadiusInput) - 1))
                using (var pen = new Pen(Theme.Primary, 1f))
                    e.Graphics.DrawPath(pen, path);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    /// <summary>Pointy's segmented button: sunken track, the chosen segment raised.</summary>
    internal sealed class SegmentedControl : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _selected;
        private int _hover = -1;

        public SegmentedControl(params string[] items)
        {
            _items.AddRange(items);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Font = Theme.BodyStrong;
            Height = Theme.S(40);
            Cursor = Cursors.Hand;
            Margin = new Padding(Theme.S(4));
            TabStop = true;
        }

        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get => _selected;
            set
            {
                if (value == _selected || value < 0 || value >= _items.Count)
                    return;
                _selected = value;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private Rectangle Segment(int index)
        {
            int inset = Theme.S(3);
            int width = (Width - inset * 2) / _items.Count;
            int visual = RightToLeft == RightToLeft.Yes ? _items.Count - 1 - index : index;
            return new Rectangle(inset + visual * width, inset, width, Height - inset * 2 - 1);
        }

        private int HitTest(Point p)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (Segment(i).Contains(p))
                    return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int hover = HitTest(e.Location);
            if (hover != _hover) { _hover = hover; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int hit = HitTest(e.Location);
            if (hit >= 0 && Enabled)
                SelectedIndex = hit;
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) => keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool rtl = RightToLeft == RightToLeft.Yes;
            if (e.KeyCode == Keys.Left)
                SelectedIndex = Math.Max(0, Math.Min(_items.Count - 1, _selected + (rtl ? 1 : -1)));
            else if (e.KeyCode == Keys.Right)
                SelectedIndex = Math.Max(0, Math.Min(_items.Count - 1, _selected + (rtl ? -1 : 1)));
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            var track = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(g, track, Theme.S(Theme.RadiusChip), Theme.SubtleFill, Theme.Line);

            for (int i = 0; i < _items.Count; i++)
            {
                Rectangle seg = Segment(i);
                bool selected = i == _selected;
                if (selected)
                    Theme.FillRounded(g, seg, Theme.S(Theme.RadiusChip) - Theme.S(2), Theme.Surface, Focused ? Theme.Primary : Theme.LineStrong);
                else if (i == _hover && Enabled)
                    Theme.FillRounded(g, seg, Theme.S(Theme.RadiusChip) - Theme.S(2), Theme.SurfaceSunken);

                Color color = !Enabled ? Theme.MutedInk : selected ? Theme.PrimaryStrong : Theme.MutedInk;
                TextRenderer.DrawText(g, _items[i], Font, seg, color,
                    Theme.TextFlags(this, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis));
            }
        }
    }

    /// <summary>Pill progress track, filling from the reading start (right in RTL).</summary>
    internal sealed class ProgressTrack : Control
    {
        private double _value;

        public ProgressTrack()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(8);
            Margin = new Padding(Theme.S(4), Theme.S(8), Theme.S(4), Theme.S(4));
        }

        public double Value
        {
            get => _value;
            set { _value = Math.Max(0, Math.Min(1, value)); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            var track = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(g, track, Height / 2, Theme.SubtleFill);
            int width = (int)Math.Round((Width - 1) * _value);
            if (width <= 0)
                return;
            width = Math.Max(width, Height);
            int x = RightToLeft == RightToLeft.Yes ? Width - 1 - width : 0;
            Theme.FillRounded(g, new Rectangle(x, 0, width, Height - 1), Height / 2, Theme.Primary);
        }
    }

    internal enum Tone
    {
        Neutral,
        Success,
        Warning,
        Danger,
    }

    /// <summary>Status pill: tinted container, toned text.</summary>
    internal sealed class Chip : Control
    {
        private Tone _tone;

        public Chip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.Label;
            Height = Theme.S(28);
            Margin = new Padding(Theme.S(4));
        }

        public void Set(string text, Tone tone)
        {
            Text = text;
            _tone = tone;
            Width = TextRenderer.MeasureText(text, Font).Width + Theme.S(28);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Surface);
            Color fill, ink;
            switch (_tone)
            {
                case Tone.Success: fill = Theme.PrimaryContainer; ink = Theme.Success; break;
                case Tone.Warning: fill = Theme.AmberContainer; ink = Theme.Warning; break;
                case Tone.Danger: fill = Theme.DangerContainer; ink = Theme.Danger; break;
                default: fill = Theme.SubtleFill; ink = Theme.MutedInk; break;
            }
            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRounded(g, bounds, Height / 2, fill);

            // Leading dot, then the label.
            int dot = Theme.S(8);
            bool rtl = RightToLeft == RightToLeft.Yes;
            int dotX = rtl ? Width - Theme.S(12) - dot : Theme.S(12);
            using (var brush = new SolidBrush(ink))
                g.FillEllipse(brush, dotX, (Height - dot) / 2, dot, dot);
            var textBounds = rtl
                ? new Rectangle(Theme.S(8), 0, Width - Theme.S(28), Height)
                : new Rectangle(Theme.S(24), 0, Width - Theme.S(28), Height);
            TextRenderer.DrawText(g, Text, Font, textBounds, ink,
                Theme.TextFlags(this, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine));
        }
    }

    /// <summary>Pointy's dark top bar: logo, دفتر, screen title.</summary>
    internal sealed class TopBar : Control
    {
        private readonly Image _logo = Theme.Logo();

        public TopBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(60);
            Dock = DockStyle.Top;
        }

        public string Title { get; set; }
        public string Trailing { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.DarkTopBar);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int pad = Theme.S(20);
            int logo = Theme.S(34);
            int x = Width - pad - logo; // RTL: brand at the right edge
            if (_logo != null)
                g.DrawImage(_logo, new Rectangle(x, (Height - logo) / 2, logo, logo));

            Font brand = Theme.Brand;
            Size brandSize = TextRenderer.MeasureText(Text.Length > 0 ? Text : " ", brand);
            int brandRight = x - Theme.S(10);
            TextRenderer.DrawText(g, Text, brand, new Rectangle(brandRight - brandSize.Width, 0, brandSize.Width, Height), Color.White,
                TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            int sep = brandRight - brandSize.Width - Theme.S(14);
            using (var pen = new Pen(Color.FromArgb(60, 255, 255, 255)))
                g.DrawLine(pen, sep, Theme.S(18), sep, Height - Theme.S(18));

            TextRenderer.DrawText(g, Title, Theme.Title, new Rectangle(pad, 0, sep - Theme.S(14) - pad, Height), Color.FromArgb(230, 255, 255, 255),
                TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (!string.IsNullOrEmpty(Trailing))
                TextRenderer.DrawText(g, Trailing, Theme.Small, new Rectangle(pad, 0, Theme.S(200), Height), Color.FromArgb(150, 255, 255, 255),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
