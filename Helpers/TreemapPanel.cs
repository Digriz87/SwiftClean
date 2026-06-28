using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SwiftClean.Helpers
{
    /// <summary>
    /// Lays its children out as a squarified treemap (Bruls/Huizing/van Wijk): each child's area is
    /// proportional to its attached <see cref="WeightProperty"/>, and the algorithm favors near-square
    /// tiles. Resizes automatically with the panel. Use inside an <c>ItemsControl.ItemsPanel</c> and set
    /// the weight via the container style: <c>helpers:TreemapPanel.Weight="{Binding Weight}"</c>.
    /// </summary>
    public class TreemapPanel : Panel
    {
        public static readonly DependencyProperty WeightProperty =
            DependencyProperty.RegisterAttached(
                "Weight", typeof(double), typeof(TreemapPanel),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

        public static void SetWeight(DependencyObject o, double v) => o.SetValue(WeightProperty, v);
        public static double GetWeight(DependencyObject o) => (double)o.GetValue(WeightProperty);

        protected override Size MeasureOverride(Size availableSize)
        {
            foreach (UIElement child in InternalChildren)
                child.Measure(availableSize);

            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var items = InternalChildren.Cast<UIElement>()
                .Select(c => (el: c, w: GetWeight(c)))
                .Where(x => x.w > 0)
                .OrderByDescending(x => x.w)
                .ToList();

            // Collapse any zero-weight children to nothing.
            foreach (UIElement c in InternalChildren)
                if (GetWeight(c) <= 0)
                    c.Arrange(new Rect(0, 0, 0, 0));

            if (items.Count == 0 || finalSize.Width <= 0 || finalSize.Height <= 0)
                return finalSize;

            double totalWeight = items.Sum(i => i.w);
            double scale = finalSize.Width * finalSize.Height / totalWeight; // weight -> px area
            var areas = items.Select(i => (i.el, area: i.w * scale)).ToList();

            Squarify(areas, new Rect(0, 0, finalSize.Width, finalSize.Height));
            return finalSize;
        }

        private static void Squarify(List<(UIElement el, double area)> items, Rect rect)
        {
            int i = 0;
            var row = new List<(UIElement el, double area)>();

            while (i < items.Count)
            {
                double side = Min(rect.Width, rect.Height);
                if (side <= 0)
                {
                    // No room left — collapse the rest.
                    for (; i < items.Count; i++) items[i].el.Arrange(new Rect(rect.X, rect.Y, 0, 0));
                    break;
                }

                var candidate = items[i];
                if (row.Count == 0 || Worst(row, candidate.area, side) <= Worst(row, 0, side))
                {
                    row.Add(candidate);
                    i++;
                }
                else
                {
                    rect = LayoutRow(row, rect);
                    row.Clear();
                }
            }

            if (row.Count > 0)
                LayoutRow(row, rect);
        }

        // Worst aspect ratio of a row laid along a side of length <paramref name="side"/>,
        // optionally including one more area (<paramref name="extra"/> = 0 to ignore).
        private static double Worst(List<(UIElement el, double area)> row, double extra, double side)
        {
            double sum = extra;
            double max = extra;
            double min = extra > 0 ? extra : double.MaxValue;
            foreach (var r in row)
            {
                sum += r.area;
                if (r.area > max) max = r.area;
                if (r.area < min) min = r.area;
            }
            if (sum <= 0 || min == double.MaxValue) return double.MaxValue;

            double s2 = sum * sum;
            double side2 = side * side;
            return Max(side2 * max / s2, s2 / (side2 * min));
        }

        // Places the row along the shorter side of the rect and returns the remaining rect.
        private static Rect LayoutRow(List<(UIElement el, double area)> row, Rect rect)
        {
            double sum = row.Sum(r => r.area);
            if (sum <= 0) return rect;

            if (rect.Width >= rect.Height)
            {
                // Vertical column on the left, width = sum / height.
                double colWidth = Min(sum / rect.Height, rect.Width);
                double y = rect.Y;
                foreach (var r in row)
                {
                    double h = colWidth > 0 ? r.area / colWidth : 0;
                    h = Min(h, rect.Y + rect.Height - y);
                    r.el.Arrange(new Rect(rect.X, y, colWidth, Max(h, 0)));
                    y += h;
                }
                return new Rect(rect.X + colWidth, rect.Y, Max(rect.Width - colWidth, 0), rect.Height);
            }
            else
            {
                // Horizontal strip on top, height = sum / width.
                double rowHeight = Min(sum / rect.Width, rect.Height);
                double x = rect.X;
                foreach (var r in row)
                {
                    double w = rowHeight > 0 ? r.area / rowHeight : 0;
                    w = Min(w, rect.X + rect.Width - x);
                    r.el.Arrange(new Rect(x, rect.Y, Max(w, 0), rowHeight));
                    x += w;
                }
                return new Rect(rect.X, rect.Y + rowHeight, rect.Width, Max(rect.Height - rowHeight, 0));
            }
        }

        private static double Min(double a, double b) => a < b ? a : b;
        private static double Max(double a, double b) => a > b ? a : b;
    }
}
