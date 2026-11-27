using System;
using System.Drawing;
using System.Windows.Forms;

namespace PromptDesk
{
    internal sealed class VariableChoiceStrip : FlowLayoutPanel
    {
        readonly Variable variable;
        readonly Action<VariableOption> onSelect;
        readonly Action onReorder;
        readonly ToolTip tips = new ToolTip();
        GlassButton pressedButton;
        Rectangle dragThreshold;
        bool dragging;
        Rectangle insertionMark;

        internal VariableOption SelectedOption { get; private set; }

        internal VariableChoiceStrip(Variable variable, Action<VariableOption> onSelect, Action onReorder)
        {
            this.variable = variable;
            this.onSelect = onSelect;
            this.onReorder = onReorder;
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            WrapContents = true;
            Margin = new Padding(0, 0, 0, 2);
            Padding = new Padding(3, 0, 0, 0);
            DoubleBuffered = true;
            SuspendLayout();
            AddChoice(null);
            foreach (var option in variable.Options) AddChoice(option);
            ResumeLayout();
        }

        void AddChoice(VariableOption option)
        {
            var button = new GlassButton {
                Text = option == null ? "Libre" : option.Label,
                Tag = option, Active = option == null, AutoSize = false,
                AccessibleRole = AccessibleRole.RadioButton,
                Margin = new Padding(0, 0, 5, 4), TabIndex = Controls.Count
            };
            var preferred = button.GetPreferredSize(Size.Empty);
            button.Size = new Size(Math.Min(preferred.Width, 280), preferred.Height);
            button.Click += delegate { if (!dragging) SelectOption(option); };
            if (option != null)
            {
                tips.SetToolTip(button, option.Label + Environment.NewLine + "Arrastrá para cambiar el orden.");
                button.MouseDown += StartPress;
                button.MouseMove += MovePress;
                button.MouseUp += FinishPress;
                button.MouseCaptureChanged += delegate {
                    if (pressedButton == button && !button.Capture) ClearPress();
                };
            }
            Controls.Add(button);
        }

        internal void SelectOption(VariableOption option)
        {
            if (option != null && !variable.Options.Contains(option)) return;
            SelectedOption = option;
            foreach (GlassButton button in Controls)
            {
                button.Active = button.Tag == option;
                button.Invalidate();
            }
            onSelect(option);
        }

        internal void FocusSelected()
        {
            foreach (GlassButton button in Controls)
                if (button.Active) { button.Focus(); return; }
        }

        // Bound the auto-sized row to its table column, so a long list wraps
        // vertically even when the parent asks for its unconstrained size.
        public override Size GetPreferredSize(Size proposedSize)
        {
            if (Parent != null && Parent.ClientSize.Width > 0)
            {
                int available = Math.Max(1, Parent.ClientSize.Width - Parent.Padding.Horizontal - Margin.Horizontal);
                if (proposedSize.Width <= 0 || proposedSize.Width > available) proposedSize.Width = available;
            }
            return base.GetPreferredSize(proposedSize);
        }

        void StartPress(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            pressedButton = (GlassButton)sender;
            var size = SystemInformation.DragSize;
            dragThreshold = new Rectangle(e.X - size.Width / 2, e.Y - size.Height / 2, size.Width, size.Height);
            pressedButton.Capture = true;
        }

        void MovePress(object sender, MouseEventArgs e)
        {
            if (sender != pressedButton || e.Button != MouseButtons.Left) return;
            if (!dragging && dragThreshold.Contains(e.Location)) return;
            dragging = true;
            Point point = PointToClient(pressedButton.PointToScreen(e.Location));
            int slot = InsertionSlot(point);
            pressedButton.Cursor = slot >= 0 ? Cursors.SizeWE : Cursors.No;
            Rectangle mark = Rectangle.Empty;
            if (slot >= 0)
            {
                bool end = slot == Controls.Count;
                var bounds = Controls[end ? slot - 1 : slot].Bounds;
                mark = new Rectangle(end ? bounds.Right + 1 : bounds.Left - 3, bounds.Top + 3, 2, bounds.Height - 6);
            }
            if (insertionMark != mark) { insertionMark = mark; Invalidate(); }
        }

        void FinishPress(object sender, MouseEventArgs e)
        {
            if (sender != pressedButton || e.Button != MouseButtons.Left) return;
            var button = pressedButton;
            var point = PointToClient(button.PointToScreen(e.Location));
            bool moved = dragging;
            ClearPress();
            button.Capture = false;
            if (moved) DropOption((VariableOption)button.Tag, point);
        }

        void ClearPress()
        {
            if (pressedButton != null) pressedButton.Cursor = Cursors.Hand;
            pressedButton = null;
            dragging = false;
            insertionMark = Rectangle.Empty;
            Invalidate();
        }

        // Slots are gaps in displayed order. Slot zero belongs to Libre.
        int InsertionSlot(Point point)
        {
            if (!ClientRectangle.Contains(point)) return -1;
            for (int i = 1; i < Controls.Count; i++)
            {
                var bounds = Controls[i].Bounds;
                if (point.Y < bounds.Top || (point.Y < bounds.Bottom && point.X < bounds.Left + bounds.Width / 2))
                    return i;
            }
            return Controls.Count;
        }

        internal void DropOption(VariableOption source, Point clientPoint)
        {
            int from = variable.Options.IndexOf(source);
            int slot = InsertionSlot(clientPoint);
            if (from < 0 || slot < 1) return;
            int to = slot - 1;
            if (to > from) to--;
            if (to == from) return;
            var button = Controls[from + 1];
            variable.Options.RemoveAt(from);
            variable.Options.Insert(to, source);
            Controls.SetChildIndex(button, to + 1);
            for (int i = 0; i < Controls.Count; i++) Controls[i].TabIndex = i;
            onReorder();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!insertionMark.IsEmpty)
            {
                using (var brush = new SolidBrush(UI.Accent)) e.Graphics.FillRectangle(brush, insertionMark);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
