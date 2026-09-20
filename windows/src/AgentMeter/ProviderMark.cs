using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AgentMeter;

// Renders the two audited, embedded vendor vectors; not a general SVG loader.
internal static class ProviderMark
{
    private static readonly Dictionary<string, (float Size, GraphicsPath[] Paths)> Marks = new();
    internal static void Draw(Graphics graphics, string provider, RectangleF bounds, Color color)
    {
        var key = provider.StartsWith("Claude", StringComparison.Ordinal) ? "Claude" : "Codex";
        lock (Marks)
        {
            if (!Marks.TryGetValue(key, out var mark))
            {
                using var stream = typeof(ProviderMark).Assembly.GetManifestResourceStream($"AgentMeter.Assets.{key}Logo.svg")!;
                var xml = XDocument.Load(stream);
                mark = (key == "Claude" ? 248 : 721, xml.Descendants().Where(n => n.Name.LocalName == "path" && n.Attribute("d") is not null)
                    .Select(n => Parse(n.Attribute("d")!.Value)).ToArray());
                Marks[key] = mark;
            }
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / mark.Size, bounds.Height / mark.Size);
            using var brush = new SolidBrush(color);
            foreach (var path in mark.Paths) graphics.FillPath(brush, path);
            graphics.Restore(state);
        }
    }
    private static GraphicsPath Parse(string source)
    {
        var tokens = Regex.Matches(source, @"[MLCHVZ]|-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?").Select(m => m.Value).ToArray();
        var path = new GraphicsPath(FillMode.Winding); var index = 0; var x = 0f; var y = 0f; var command = 'M';
        float Number() => float.Parse(tokens[index++], CultureInfo.InvariantCulture);
        while (index < tokens.Length)
        {
            if (tokens[index].Length == 1 && char.IsLetter(tokens[index][0])) command = tokens[index++][0];
            switch (command)
            {
                case 'M': x = Number(); y = Number(); path.StartFigure(); command = 'L'; break;
                case 'L': { var nx = Number(); var ny = Number(); path.AddLine(x, y, nx, ny); x = nx; y = ny; break; }
                case 'H': { var nx = Number(); path.AddLine(x, y, nx, y); x = nx; break; }
                case 'V': { var ny = Number(); path.AddLine(x, y, x, ny); y = ny; break; }
                case 'C': { var a = Number(); var b = Number(); var c = Number(); var d = Number(); var nx = Number(); var ny = Number(); path.AddBezier(x, y, a, b, c, d, nx, ny); x = nx; y = ny; break; }
                case 'Z': path.CloseFigure(); break;
                default: throw new InvalidOperationException("Unsupported embedded vector.");
            }
        }
        return path;
    }
}
