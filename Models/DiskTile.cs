using SwiftClean.Helpers;

namespace SwiftClean.Models
{
    /// <summary>
    /// One rectangle in the disk usage treemap: a labeled, colored slice whose area is
    /// proportional to <see cref="Bytes"/>. Used for the finviz-style application map
    /// (one tile per installed program, colored by how recently it was used).
    /// </summary>
    public class DiskTile
    {
        public DiskTile(string name, long bytes, string caption, string bgHex, string textHex, string borderHex)
        {
            Name = name;
            Bytes = bytes;
            Caption = caption;
            BgHex = bgHex;
            TextHex = textHex;
            BorderHex = borderHex;
        }

        public string Name { get; }
        public long Bytes { get; }
        public string SizeText => SizeFormatter.Format(Bytes);

        /// <summary>Secondary line (e.g. last-used) shown on larger tiles and as the tooltip.</summary>
        public string Caption { get; }
        public string ToolTipText => $"{Name}  ·  {SizeText}\n{Caption}";

        /// <summary>Tile area weight for the treemap. Floored so tiny-but-present items stay visible.</summary>
        public double Weight => System.Math.Max(Bytes, 1);

        public string BgHex { get; }
        public string TextHex { get; }
        public string BorderHex { get; }
    }
}
