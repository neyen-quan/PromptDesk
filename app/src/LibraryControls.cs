using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PromptDesk
{
    public sealed class LibraryList : ListBox
    {
        readonly Font titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        readonly Font previewFont = new Font("Segoe UI", 9.5f);
        readonly Font metaFont = new Font("Segoe UI", 8);

        public LibraryList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 108;
            IntegralHeight = false;
            BorderStyle = BorderStyle.None;
            BackColor = UI.Panel;
            ForeColor = UI.Ink;
            HorizontalScrollbar = false;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using (var background = new SolidBrush(UI.Panel)) e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var entry = (Entry)Items[e.Index];
            bool active = (e.State & DrawItemState.Selected) != 0;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(e.Bounds.Left + 2, e.Bounds.Top + 3, Math.Max(8, e.Bounds.Width - 5), e.Bounds.Height - 7);
            using (var path = Glass.Round(bounds, 10))
            using (var fill = new SolidBrush(active ? UI.Selected : Color.FromArgb(25, 36, 49)))
            using (var line = new Pen(active ? Color.FromArgb(83, 160, 184) : UI.Line))
            {
                e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(line, path);
            }
            string name = String.IsNullOrWhiteSpace(entry.Get("name")) ? "Sin título" : entry.Get("name");
            var title = new Rectangle(bounds.Left + 13, bounds.Top + (entry.Archived ? 3 : 11), Math.Max(1, bounds.Width - 43), 22);
            TextRenderer.DrawText(e.Graphics, Fit(name, titleFont, title.Width), titleFont, title, UI.Ink, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (entry.Favorite) Glyph.Draw(e.Graphics, "star", new Rectangle(bounds.Right - 25, title.Top + 2, 14, 14), UI.Accent);
            if (entry.Archived)
            {
                string location = String.Join("  /  ", new[] { entry.Get("collection"), entry.Get("category") }.Where(s => s.Length != 0));
                TextRenderer.DrawText(e.Graphics, location, metaFont,
                    new Rectangle(bounds.Left + 13, bounds.Top + 25, Math.Max(1, bounds.Width - 26), 19),
                    UI.Muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3), UI.Accent, UI.Selected);
                return;
            }
            string preview = entry.Get("description");
            if (String.IsNullOrWhiteSpace(preview)) preview = entry.Get("prompt");
            if (String.IsNullOrWhiteSpace(preview)) preview = "Escribí tu prompt en el editor…";
            preview = preview.Replace("\r", " ").Replace("\n", " ");
            if (preview.Length > 250) preview = preview.Substring(0, 250);
            TextRenderer.DrawText(e.Graphics, Fit(preview, previewFont, bounds.Width - 26), previewFont,
                new Rectangle(bounds.Left + 13, bounds.Top + 37, Math.Max(1, bounds.Width - 26), 24),
                UI.Muted, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string metadata = String.Join("  ·  ", new[] { entry.Get("category"), entry.Get("model") }.Where(s => !String.IsNullOrWhiteSpace(s)).ToArray());
            if (metadata.Length == 0) metadata = "Sin categoría · " + entry.Get("collection");
            TextRenderer.DrawText(e.Graphics, Fit(metadata, metaFont, bounds.Width - 26), metaFont,
                new Rectangle(bounds.Left + 13, bounds.Top + 72, Math.Max(1, bounds.Width - 26), 18),
                active ? UI.Accent : Color.FromArgb(126, 157, 181), TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3), UI.Accent, UI.Selected);
        }

        static string Fit(string text, Font font, int width)
        {
            if (TextRenderer.MeasureText(text, font).Width <= width) return text;
            int low = 0, high = text.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (TextRenderer.MeasureText(text.Substring(0, middle) + "…", font).Width <= width) low = middle;
                else high = middle - 1;
            }
            if (low > 0 && Char.IsHighSurrogate(text[low - 1])) low--;
            return text.Substring(0, low).TrimEnd() + "…";
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (SelectedIndex < 0) return;
            int visibleRows = Math.Max(1, ClientSize.Height / ItemHeight);
            if (SelectedIndex >= TopIndex + visibleRows) TopIndex = Math.Max(0, SelectedIndex - visibleRows + 1);
            else if (SelectedIndex < TopIndex) TopIndex = SelectedIndex;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { titleFont.Dispose(); previewFont.Dispose(); metaFont.Dispose(); }
            base.Dispose(disposing);
        }
    }

    public static class Glyph
    {
        public static void Draw(Graphics graphics, string name, Rectangle bounds, Color color)
        {
            var state = graphics.Save();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 20f, bounds.Height / 20f);
            using (var pen = new Pen(color, 1.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (name == "plus") { graphics.DrawLine(pen, 10, 4, 10, 16); graphics.DrawLine(pen, 4, 10, 16, 10); }
                else if (name == "close") { graphics.DrawLine(pen, 5, 5, 15, 15); graphics.DrawLine(pen, 15, 5, 5, 15); }
                else if (name == "search") { graphics.DrawEllipse(pen, 3, 3, 10, 10); graphics.DrawLine(pen, 12, 12, 17, 17); }
                else if (name == "copy") { graphics.DrawRectangle(pen, 7, 6, 10, 12); graphics.DrawLines(pen, new[] { new Point(12, 3), new Point(3, 3), new Point(3, 13) }); }
                else if (name == "library") { graphics.DrawRectangle(pen, 3, 3, 14, 14); graphics.DrawLine(pen, 7, 3, 7, 17); graphics.DrawLine(pen, 10, 7, 14, 7); graphics.DrawLine(pen, 10, 11, 14, 11); }
                else if (name == "clock") { graphics.DrawEllipse(pen, 2, 2, 16, 16); graphics.DrawLines(pen, new[] { new Point(10, 5), new Point(10, 10), new Point(14, 12) }); }
                else if (name == "archive") { graphics.DrawRectangle(pen, 2, 3, 16, 4); graphics.DrawRectangle(pen, 4, 7, 12, 10); graphics.DrawLine(pen, 8, 10, 12, 10); }
                else if (name == "star")
                {
                    var points = new PointF[10];
                    for (int i = 0; i < points.Length; i++) { double angle = -Math.PI / 2 + i * Math.PI / 5; float radius = i % 2 == 0 ? 8 : 3.7f; points[i] = new PointF(10 + (float)Math.Cos(angle) * radius, 10 + (float)Math.Sin(angle) * radius); }
                    graphics.DrawPolygon(pen, points);
                }
                else if (name == "menu") { for (int i = 0; i < 3; i++) using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, 3 + i * 6, 9, 2, 2); }
                else if (name == "settings") { graphics.DrawLine(pen, 3, 5, 17, 5); graphics.DrawLine(pen, 3, 10, 17, 10); graphics.DrawLine(pen, 3, 15, 17, 15); graphics.DrawRectangle(pen, 6, 3, 3, 4); graphics.DrawRectangle(pen, 12, 8, 3, 4); graphics.DrawRectangle(pen, 6, 13, 3, 4); }
                else if (name == "refresh") { graphics.DrawArc(pen, 3, 3, 14, 14, -45, 300); graphics.DrawLines(pen, new[] { new Point(17, 2), new Point(17, 7), new Point(12, 7) }); }
                else if (name == "braces") { graphics.DrawLines(pen, new[] { new Point(7, 3), new Point(4, 3), new Point(4, 8), new Point(2, 10), new Point(4, 12), new Point(4, 17), new Point(7, 17) }); graphics.DrawLines(pen, new[] { new Point(13, 3), new Point(16, 3), new Point(16, 8), new Point(18, 10), new Point(16, 12), new Point(16, 17), new Point(13, 17) }); }
            }
            graphics.Restore(state);
        }

        public static void EmptyLibrary(Graphics graphics, Rectangle bounds)
        {
            int x = (bounds.Width - 66) / 2, y = (bounds.Height - 66) / 2;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Glass.Round(new Rectangle(x, y, 66, 66), 18))
            using (var fill = new SolidBrush(Color.FromArgb(29, 53, 68))) graphics.FillPath(fill, path);
            Draw(graphics, "library", new Rectangle(x + 18, y + 17, 32, 32), UI.Accent);
        }
    }
}
