using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAArmorAttack.Engine;
using Effect = AVAArmorAttack.Engine.Effect;

namespace AVAArmorAttack.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAArmorAttack/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);



    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private const uint Olive = 0xFF7AFF8A, Steel = 0xFF6AE0FF, Rotor = 0xFFFF6AD0, Shellfire = 0xFFFF9A4A;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>The colours of the buildings, washed over the streets like the coloured overlay on an old vector cabinet.</summary>
    private static readonly uint[] Roofs = [0xFF5AD08A, 0xFF5AA8E0, 0xFFE0B05A, 0xFFB08AE0, 0xFF5AD0C8, 0xFFE07A6A];

    private static uint RoofColour(char b) => Roofs[(b * 7 + 3) % Roofs.Length];

    /// <summary>A district's building edges, joined into long lines, worked out once.</summary>
    private static readonly Dictionary<City, List<(Point A, Point B, uint Colour)>> Edges = [];

    private static List<(Point A, Point B, uint Colour)> EdgesOf(City city)
    {
        if (Edges.TryGetValue(city, out var list)) return list;
        list = [];
        // Along each row boundary, then each column boundary: a run of cells of one building against something else.
        for (int r = 0; r <= Field.Rows; r++)
        for (int side = 0; side < 2; side++)
        {
            int start = -1;
            char run = '.';
            for (int c = 0; c <= Field.Cols; c++)
            {
                char me = c < Field.Cols ? Inside(city, c, side == 0 ? r : r - 1) : '.', other = c < Field.Cols ? Inside(city, c, side == 0 ? r - 1 : r) : '.';
                char edge = me != '.' && me != other ? me : '.';
                if (edge == run) continue;
                if (run != '.') list.Add((new Point(start * Field.Cell, Field.Top + r * Field.Cell), new Point(c * Field.Cell, Field.Top + r * Field.Cell), RoofColour(run)));
                run = edge;
                start = c;
            }
        }
        for (int c = 0; c <= Field.Cols; c++)
        for (int side = 0; side < 2; side++)
        {
            int start = -1;
            char run = '.';
            for (int r = 0; r <= Field.Rows; r++)
            {
                char me = r < Field.Rows ? Inside(city, side == 0 ? c : c - 1, r) : '.', other = r < Field.Rows ? Inside(city, side == 0 ? c - 1 : c, r) : '.';
                char edge = me != '.' && me != other ? me : '.';
                if (edge == run) continue;
                if (run != '.') list.Add((new Point(c * Field.Cell, Field.Top + start * Field.Cell), new Point(c * Field.Cell, Field.Top + r * Field.Cell), RoofColour(run)));
                run = edge;
                start = r;
            }
        }
        Edges[city] = list;
        return list;
    }

    /// <summary>The building in a cell, with outside the city counting as street (so the edge of the city draws no wall).</summary>
    private static char Inside(City city, int c, int r) => c < 0 || r < 0 || c >= Field.Cols || r >= Field.Rows ? '.' : city.Plan[r][c];

    /// <summary>Each building's cells' bounds, for the roof details.</summary>
    private static IEnumerable<(char B, int C0, int R0, int C1, int R1, bool Square)> Buildings(City city)
    {
        var seen = new HashSet<(int, int)>();
        for (int r = 0; r < Field.Rows; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            char b = city.Plan[r][c];
            if (b == '.' || !seen.Add((c, r))) continue;
            // The whole of this building, by flood.
            var stack = new Stack<(int, int)>([(c, r)]);
            int c0 = c, c1 = c, r0 = r, r1 = r, n = 0;
            while (stack.Count > 0)
            {
                var (x, y) = stack.Pop();
                n++;
                (c0, c1, r0, r1) = (Math.Min(c0, x), Math.Max(c1, x), Math.Min(r0, y), Math.Max(r1, y));
                foreach (var (dx, dy) in City.Steps)
                    if (Inside(city, x + dx, y + dy) == b && seen.Add((x + dx, y + dy))) stack.Push((x + dx, y + dy));
            }
            yield return (b, c0, r0, c1, r1, n == (c1 - c0 + 1) * (r1 - r0 + 1));
        }
    }

    private void DrawCity(DrawingContext dc, City city, double alpha)
    {
        // The streets: lane marks down the middle of the wide ones.
        for (int r = 0; r < Field.Rows - 1; r++)
        for (int c = 0; c < Field.Cols; c++)
            if (Inside(city, c, r) == '.' && Inside(city, c, r + 1) == '.' && Inside(city, c, r - 1) != '.' && Inside(city, c, r + 2) != '.' && c % 2 == 0)
                dc.DrawLine(Pen(Alpha(0xFF8A90A8, 0.22 * alpha), 2), new Point(c * Field.Cell + 6, Field.Top + (r + 1) * Field.Cell), new Point(c * Field.Cell + 22, Field.Top + (r + 1) * Field.Cell));
        for (int c = 0; c < Field.Cols - 1; c++)
        for (int r = 0; r < Field.Rows; r++)
            if (Inside(city, c, r) == '.' && Inside(city, c + 1, r) == '.' && Inside(city, c - 1, r) != '.' && Inside(city, c + 2, r) != '.' && r % 2 == 0)
                dc.DrawLine(Pen(Alpha(0xFF8A90A8, 0.22 * alpha), 2), new Point((c + 1) * Field.Cell, Field.Top + r * Field.Cell + 6), new Point((c + 1) * Field.Cell, Field.Top + r * Field.Cell + 22));
        for (int r = 0; r < Field.Rows; r++)
        for (int c = 0; c < Field.Cols; c++)
        {
            char b = city.Plan[r][c];
            if (b != '.') dc.FillRectangle(Brush(Alpha(RoofColour(b), 0.13 * alpha)), new Rect(c * Field.Cell, Field.Top + r * Field.Cell, Field.Cell, Field.Cell));
        }
        foreach (var (b, c0, r0, c1, r1, square) in Buildings(city))
        {
            uint col = RoofColour(b);
            double x0 = c0 * Field.Cell, y0 = Field.Top + r0 * Field.Cell, x1 = (c1 + 1) * Field.Cell, y1 = Field.Top + (r1 + 1) * Field.Cell;
            if (!square) continue;
            // A parapet just inside the edge, and something on the roof.
            dc.DrawRectangle(null, Pen(Alpha(col, 0.35 * alpha), 1), new Rect(x0 + 6, y0 + 6, x1 - x0 - 12, y1 - y0 - 12));
            uint h = Hash(c0, r0, b);
            double w = x1 - x0, ht = y1 - y0, cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
            switch (h % 4)
            {
                case 0 when w >= 96 && ht >= 96:
                    // A helipad.
                    dc.DrawEllipse(null, Pen(Alpha(col, 0.5 * alpha), 1.4), new Point(cx, cy), 20, 20);
                    dc.DrawLine(Pen(Alpha(col, 0.5 * alpha), 1.4), new Point(cx - 7, cy - 9), new Point(cx - 7, cy + 9));
                    dc.DrawLine(Pen(Alpha(col, 0.5 * alpha), 1.4), new Point(cx + 7, cy - 9), new Point(cx + 7, cy + 9));
                    dc.DrawLine(Pen(Alpha(col, 0.5 * alpha), 1.4), new Point(cx - 7, cy), new Point(cx + 7, cy));
                    break;
                case 1:
                    // Vents in a row.
                    for (double x = x0 + 18; x < x1 - 18; x += 22)
                        dc.DrawRectangle(null, Pen(Alpha(col, 0.45 * alpha), 1), new Rect(x - 5, cy - 5, 10, 10));
                    break;
                case 2:
                    // Skylights: a ridge down the long way.
                    if (w >= ht) dc.DrawLine(Pen(Alpha(col, 0.45 * alpha), 1.2), new Point(x0 + 14, cy), new Point(x1 - 14, cy));
                    else dc.DrawLine(Pen(Alpha(col, 0.45 * alpha), 1.2), new Point(cx, y0 + 14), new Point(cx, y1 - 14));
                    break;
                default:
                    // A water tank on legs.
                    dc.DrawEllipse(null, Pen(Alpha(col, 0.5 * alpha), 1.2), new Point(x0 + 20, y0 + 20), 7, 7);
                    dc.DrawRectangle(null, Pen(Alpha(col, 0.4 * alpha), 1), new Rect(x1 - 26, y1 - 22, 14, 10));
                    break;
            }
        }
        foreach (var (a, b, col) in EdgesOf(city)) Glow(dc, a, b, col, 0.85 * alpha, 1.6);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF04060C), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame)
        {
            using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Bottom - Field.Top)))
            {
                DrawCity(dc, g.City, 1);
                foreach (var w in g.Wrecks) DrawWreck(dc, w);
                foreach (var t in g.Tanks) DrawTank(dc, t.X, t.Y, t.Angle, t.Turret, t.Damaged, t.Hit > 0, 1);
                DrawJeep(dc);
                foreach (var d in g.Wreckage)
                {
                    double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
                    Glow(dc, new Point(d.X - c, d.Y - s), new Point(d.X + c, d.Y + s), d.Colour, a, 1.6);
                }
                foreach (var b in g.Shells) DrawShell(dc, b);
                if (g.Gunship is { } u) DrawGunship(dc, u.X, u.Y, u.Angle, u.T, 1);
            }
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 8), 22, Yellow, center: true);
    }

    // ───────────────────────────── the jeep ─────────────────────────────

    private void DrawJeep(DrawingContext dc)
    {
        var j = Engine.Jeep;
        if (!j.Alive) return;
        if (j.Safe > 0 && (int)(_clock * 10) % 2 == 0) return;
        DrawJeepAt(dc, j.X, j.Y, j.Angle, j.Aim, 1);
    }

    /// <summary>The jeep from above: a squared-off body, the wheels at the corners, the windscreen, and the gun on its post.</summary>
    private static void DrawJeepAt(DrawingContext dc, double x, double y, double angle, double aim, double k)
    {
        Shape(dc, x, y, angle, k, Olive, 1, (13, -7), (13, 7), (-12, 7), (-12, -7));
        Shape(dc, x, y, angle, k, Olive, 0.8, (5, -7), (7, 0), (5, 7));
        foreach (var (wx, wy) in new[] { (8.0, -9.5), (8.0, 9.5), (-7.0, -9.5), (-7.0, 9.5) })
            Shape(dc, x, y, angle, k, Olive, 0.7, (wx + 3.5, wy - 1.5), (wx + 3.5, wy + 1.5), (wx - 3.5, wy + 1.5), (wx - 3.5, wy - 1.5));
        double gx = x - Math.Cos(angle) * 3 * k, gy = y - Math.Sin(angle) * 3 * k;
        dc.DrawEllipse(null, Pen(0xFFE8FFE8, 1.4), new Point(gx, gy), 3.5 * k, 3.5 * k);
        Glow(dc, new Point(gx, gy), new Point(gx + Math.Cos(aim) * 17 * k, gy + Math.Sin(aim) * 17 * k), 0xFFE8FFE8, 1, 2.2);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>A tank from above: the hull and its tracks, the turret, the long gun. Smoking once its engine's hit.</summary>
    private void DrawTank(DrawingContext dc, double x, double y, double angle, double turret, bool damaged, bool flash, double k)
    {
        uint col = flash ? 0xFFFFFFFF : Steel;
        Shape(dc, x, y, angle, k, col, 1, (15, -10), (15, 10), (-15, 10), (-15, -10));
        // The tracks, with their links rolling.
        double roll = (x + y) * 0.35 % 5;
        foreach (int side in new[] { -1, 1 })
        {
            Shape(dc, x, y, angle, k, col, 0.6, (16, side * 13), (-16, side * 13), (-16, side * 9.5), (16, side * 9.5));
            for (double t = -14 + roll; t < 15; t += 5)
            {
                double c = Math.Cos(angle), s = Math.Sin(angle);
                Point P(double fx, double fy) => new(x + (fx * c - fy * s) * k, y + (fx * s + fy * c) * k);
                dc.DrawLine(Pen(Alpha(col, 0.45), 1), P(t, side * 9.5), P(t, side * 13));
            }
        }
        Shape(dc, x, y, turret, k, col, 1, (7, -6), (7, 6), (-6, 7), (-8, 0), (-6, -7));
        Glow(dc, new Point(x + Math.Cos(turret) * 7 * k, y + Math.Sin(turret) * 7 * k), new Point(x + Math.Cos(turret) * 23 * k, y + Math.Sin(turret) * 23 * k), col, 1, 2.6);
        if (damaged) Smoke(dc, x - Math.Cos(angle) * 10, y - Math.Sin(angle) * 10, (int)(x * 13 + y), 0.8);
    }

    /// <summary>Puffs of smoke rising from a point and drifting off.</summary>
    private void Smoke(DrawingContext dc, double x, double y, int salt, double strength)
    {
        for (int i = 0; i < 4; i++)
        {
            double t = (_clock * 0.9 + i / 4.0 + (Hash(salt, i, 5) % 100) / 400.0) % 1;
            double r = 3 + t * 10, a = (1 - t) * 0.35 * strength;
            dc.DrawEllipse(Brush(Alpha(0xFF9AA0B0, a)), null, new Point(x + t * 14 + Math.Sin(t * 6 + i) * 3, y - t * 26), r, r);
        }
    }

    private void DrawWreck(DrawingContext dc, Wreck w)
    {
        double fade = Math.Clamp((Wreck.Life - w.Age) / 1.5, 0, 1);
        Shape(dc, w.X, w.Y, w.Angle, 1, 0xFF8A8A9A, 0.5 * fade, (15, -10), (15, 10), (-15, 10), (-15, -10));
        Shape(dc, w.X, w.Y, w.Angle + 0.6, 0.9, 0xFF8A8A9A, 0.4 * fade, (8, -2), (-8, 2), (6, 6));
        if ((int)(_clock * 14 + w.X) % 3 != 0) dc.DrawEllipse(Brush(Alpha(0xFFFF8A2A, 0.7 * fade)), null, new Point(w.X + Math.Sin(_clock * 9) * 2, w.Y), 4, 4);
        Smoke(dc, w.X, w.Y, (int)w.X, 1.3 * fade);
    }

    /// <summary>The gunship: its shadow on the streets below, the body and tail, and the rotor going round.</summary>
    private static void DrawGunship(DrawingContext dc, double x, double y, double angle, double t, double k)
    {
        Shape(dc, x + 22 * k, y + 26 * k, angle, k, 0xFF000000, 0.55, (14, 0), (6, 8), (-10, 6), (-34, 2), (-34, -2), (-10, -6), (6, -8));
        Shape(dc, x, y, angle, k, Rotor, 1, (16, 0), (8, 9), (-8, 8), (-12, 3), (-36, 2), (-36, -2), (-12, -3), (-8, -8), (8, -9));
        Shape(dc, x, y, angle, k, Rotor, 0.8, (-32, -7), (-30, -7), (-30, 7), (-32, 7));
        Shape(dc, x, y, angle, k, 0xFFFFD0F0, 0.8, (14, 0), (8, 5), (6, 0), (8, -5));
        double r = t * 26;
        for (int i = 0; i < 2; i++)
        {
            double a = r + i * Math.PI / 2;
            Glow(dc, new Point(x - Math.Cos(a) * 26 * k, y - Math.Sin(a) * 26 * k), new Point(x + Math.Cos(a) * 26 * k, y + Math.Sin(a) * 26 * k), 0xFFFFE0F8, 0.7, 1.4);
        }
        dc.DrawEllipse(null, Pen(Alpha(Rotor, 0.18), 1), new Point(x, y), 26 * k, 26 * k);
    }

    private static void DrawShell(DrawingContext dc, Shell b)
    {
        double l = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
        if (!b.Hostile)
        {
            Glow(dc, new Point(b.X - b.Vx / l * 9, b.Y - b.Vy / l * 9), new Point(b.X, b.Y), 0xFFFFFFE0, 1, 2.6);
            return;
        }
        if (b.Rocket)
        {
            Glow(dc, new Point(b.X - b.Vx / l * 14, b.Y - b.Vy / l * 14), new Point(b.X, b.Y), Rotor, 1, 2.2);
            dc.DrawEllipse(Brush(0xFFFFE0A0), null, new Point(b.X - b.Vx / l * 15, b.Y - b.Vy / l * 15), 2.2, 2.2);
            return;
        }
        dc.DrawEllipse(Brush(Alpha(Shellfire, 0.3)), null, new Point(b.X, b.Y), 7, 7);
        dc.DrawEllipse(Brush(0xFFFFD08A), null, new Point(b.X, b.Y), 3.2, 3.2);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                dc.DrawEllipse(Brush(Alpha(e.Colour, 0.3 * (1 - t))), null, c, r * 0.6, r * 0.6);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 20, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"WAVE {g.Wave}  {g.City.Name}", new Point(512, 18), 26, Olive, center: true);
        // The tanks still to beat: the ones out there, and the ones to come.
        int left = g.Tanks.Count + g.TanksToCome;
        for (int i = 0; i < Math.Min(left, 16); i++) DrawTankIcon(dc, 512 - 7.5 * 16 + 8 + i * 15, 52);
        int spare = Math.Max(0, g.Lives - (g.Jeep.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawJeepAt(dc, 196 + i * 30, 48, -Math.PI / 2, -Math.PI / 2, 0.75);
    }

    private static void DrawTankIcon(DrawingContext dc, double x, double y)
    {
        dc.DrawRectangle(null, Pen(Alpha(Steel, 0.8), 1.2), new Rect(x - 5, y - 6, 10, 12));
        dc.DrawLine(Pen(Alpha(Steel, 0.8), 1.4), new Point(x, y), new Point(x, y - 10));
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 250;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0060A10), new Rect(cx - w / 2, cy - 46, w, h), 6);
        dc.DrawRectangle(null, Pen(Alpha(Olive, 0.5), 1.2), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        // The Old Town, faint, behind it all.
        DrawCity(dc, Cities.All[0], 0.25);
        dc.FillRectangle(Brush(0x9004060C), new Rect(0, Field.Top, Field.Width, Field.Bottom - Field.Top));
        string title = "AVA ARMOR ATTACK";
        uint[] hues = [Olive, 0xFFB0FFB0, Steel, 0xFFFFE04A];
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 24 + 24 + i * 48, 120), 100, hues[(i + (int)(_clock * 3)) % hues.Length], center: true);
        Text(dc, "AFTER THE 1980 VECTOR ARCADE CLASSIC", new Point(cx, 184), 28, Dim, center: true);
        // A jeep and a tank facing off down a street.
        double sway = Math.Sin(_clock * 1.3) * 0.35;
        DrawJeepAt(dc, cx - 120, 262, 0, sway, 1.8);
        DrawTank(dc, cx + 120, 262, Math.PI, Math.PI - sway * 0.6, false, false, 1.6);
        if ((int)(_clock * 2.5) % 3 == 0) Glow(dc, new Point(cx - 70, 262), new Point(cx - 40, 262), 0xFFFFFFE0, 1, 3);
        Text(dc, "DRIVE THE STREETS AND KNOCK OUT EVERY TANK - BUILDINGS STOP SHELLS, BUT NOT THE GUNSHIP", new Point(cx, 332), 22, White, center: true);
        (string Name, string Points, Action<double> Draw)[] legend =
        [
            ("TANK", "100 + 400", x => DrawTank(dc, x, 410, -Math.PI / 2 + Math.Sin(_clock) * 0.3, -Math.PI / 2 + _clock, false, false, 1.4)),
            ("DAMAGED", "400", x => DrawTank(dc, x, 410, -Math.PI / 2, -Math.PI / 2 - _clock * 0.7, true, false, 1.4)),
            ("GUNSHIP", "1000", x => DrawGunship(dc, x, 404, -Math.PI / 2 + Math.Sin(_clock * 0.8) * 0.5, _clock, 1.1)),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = cx - 220 + i * 220;
            legend[i].Draw(x);
            Text(dc, legend[i].Name, new Point(x, 470), 22, Dim, center: true);
            Text(dc, legend[i].Points, new Point(x, 496), 22, Yellow, center: true);
        }
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 570), 46, Yellow, center: true);
        Text(dc, "ARROWS: STEER, FORWARD AND BACK    Z / X: SWING THE GUN    SPACE: FIRE", new Point(cx, 640), 23, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA JEEP EVERY {GameEngine.ExtraLifeEvery}    GAMEPAD: STICK, BUMPERS, A", new Point(cx, 672), 22, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP COMMANDERS", new Point(cx, 140), 68, Olive, center: true);
        Text(dc, "RANK NAME    SCORE    WAVE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
