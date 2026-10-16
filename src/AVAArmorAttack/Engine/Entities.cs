namespace AVAArmorAttack.Engine;

/// <summary>
/// The controls: steering (-1 left, 1 right), driving (1 forward, -1 back), swivelling the gun (-1 anticlockwise,
/// 1 clockwise), and fire.
/// </summary>
public readonly record struct Controls(int Turn = 0, int Drive = 0, int Swivel = 0, bool Fire = false);

/// <summary>
/// The screen, and the city on it: a grid of cells below the score bar, each either street or building.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Cols = 32, Rows = 21;
    public const double Cell = 32, Top = 80;
    public const double Bottom = Top + Rows * Cell;

    public static double CellX(double col) => (col + 0.5) * Cell;
    public static double CellY(double row) => Top + (row + 0.5) * Cell;
    public static int ColOf(double x) => (int)Math.Floor(x / Cell);
    public static int RowOf(double y) => (int)Math.Floor((y - Top) / Cell);
    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
}

/// <summary>
/// One district of the city: its plan, a row of characters for each row of cells ('.' for street, a letter for a
/// building: cells with the same letter that touch are one building), and where a new jeep starts.
/// </summary>
public sealed class City
{
    public string Name { get; }
    public string[] Plan { get; }
    public double StartX { get; }
    public double StartY { get; }

    public City(string name, double startCol, double startRow, params string[] plan)
    {
        Name = name;
        Plan = plan;
        StartX = startCol * Field.Cell;
        StartY = Field.Top + startRow * Field.Cell;
    }

    /// <summary>The building in a cell, or '.' for street; outside the city counts as a wall.</summary>
    public char At(int col, int row) => col < 0 || row < 0 || col >= Field.Cols || row >= Field.Rows ? '#' : Plan[row][col];

    public bool Solid(int col, int row) => At(col, row) != '.';
    public bool SolidAt(double x, double y) => Solid(Field.ColOf(x), Field.RowOf(y));

    /// <summary>Whether a shot could fly straight from one point to the other without hitting a building.</summary>
    public bool Clear(double ax, double ay, double bx, double by)
    {
        double d = Field.Distance(ax, ay, bx, by);
        int n = Math.Max(1, (int)(d / 6));
        for (int i = 1; i < n; i++)
        {
            double t = (double)i / n;
            if (SolidAt(ax + (bx - ax) * t, ay + (by - ay) * t)) return false;
        }
        return true;
    }

    /// <summary>
    /// How many steps along the streets (up, down, left, right) every cell is from a starting cell; -1 where the
    /// streets don't reach, or past cells that <paramref name="blocked"/> says are in the way.
    /// </summary>
    public int[,] Distances(int col, int row, Func<int, int, bool>? blocked = null)
    {
        var d = new int[Field.Cols, Field.Rows];
        for (int c = 0; c < Field.Cols; c++) for (int r = 0; r < Field.Rows; r++) d[c, r] = -1;
        if (Solid(col, row)) return d;
        var queue = new Queue<(int, int)>();
        d[col, row] = 0;
        queue.Enqueue((col, row));
        while (queue.Count > 0)
        {
            var (c, r) = queue.Dequeue();
            foreach (var (dc, dr) in Steps)
            {
                int nc = c + dc, nr = r + dr;
                if (Solid(nc, nr) || d[nc, nr] >= 0 || (blocked?.Invoke(nc, nr) ?? false)) continue;
                d[nc, nr] = d[c, r] + 1;
                queue.Enqueue((nc, nr));
            }
        }
        return d;
    }

    public static readonly (int Dc, int Dr)[] Steps = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Pushes a circle out of any building it overlaps (and inside the city's edge); true if it had to.</summary>
    public bool Push(ref double x, ref double y, double r)
    {
        bool pushed = false;
        int c0 = Field.ColOf(x - r), c1 = Field.ColOf(x + r), r0 = Field.RowOf(y - r), r1 = Field.RowOf(y + r);
        for (int row = r0; row <= r1; row++)
        for (int col = c0; col <= c1; col++)
        {
            if (!Solid(col, row)) continue;
            double left = col * Field.Cell, top = Field.Top + row * Field.Cell;
            double nx = Math.Clamp(x, left, left + Field.Cell), ny = Math.Clamp(y, top, top + Field.Cell);
            double dx = x - nx, dy = y - ny, d = Math.Sqrt(dx * dx + dy * dy);
            if (d >= r) continue;
            if (d < 1e-6)
            {
                // The middle is inside the cell: out the nearest side.
                double l = x - left, rt = left + Field.Cell - x, t = y - top, b = top + Field.Cell - y, m = Math.Min(Math.Min(l, rt), Math.Min(t, b));
                if (m == l) x = left - r; else if (m == rt) x = left + Field.Cell + r; else if (m == t) y = top - r; else y = top + Field.Cell + r;
            }
            else
            {
                x = nx + dx / d * r;
                y = ny + dy / d * r;
            }
            pushed = true;
        }
        return pushed;
    }
}

public static class Cities
{
    public static readonly City[] All =
    [
        new("THE OLD TOWN", 16, 14,
            "................................",
            "................................",
            "..AAAAA..BBBBBB..CCC.DD..EEEEE..",
            "..AAAAA..BBBBBB..CCC.DD..EEEEE..",
            "..AAAAA..BBBBBB..CCC.....EEEEE..",
            "..AAAAA..BBBBBB..CCC.FF..EEEEE..",
            "................................",
            "................................",
            "..GG.HH..IIIIII..JJJJJJ..KKKKK..",
            "..GG.HH..I....I..JJJJJJ..KKKKK..",
            "..GG.HH..I....I..........KKKKK..",
            "..GG.HH..I....I..LLLLLL..KKKKK..",
            "..GG.HH..II..II..LLLLLL..KKKKK..",
            "................................",
            "................................",
            "..MMMMM..NNN.OO..PPPPPP..QQ.RR..",
            "..MMMMM..NNN.OO..PPPPPP..QQ.RR..",
            "..MMMMM..NNN.....PPPPPP..QQ.RR..",
            "..MMMMM..NNN.SS..PPPPPP..QQ.RR..",
            "................................",
            "................................"),
        new("THE DOCKS", 16, 15,
            "................................",
            "................................",
            "..AAAAAAAAAAAA....BBBBBBBBBBBB..",
            "..AAAAAAAAAAAA....BBBBBBBBBBBB..",
            "................................",
            "..CCCC..DDDD..EE..FF..GGGG..HH..",
            "..CCCC..DDDD..EE..FF..GGGG..HH..",
            "..CCCC..DDDD..EE..FF..GGGG..HH..",
            "................................",
            "................................",
            "..IIII..JJJJ..........KKKK..LL..",
            "..IIII..JJJJ..MMMMMM..KKKK..LL..",
            "..IIII..JJJJ..MMMMMM..KKKK..LL..",
            "..IIII........MMMMMM........LL..",
            "................................",
            "................................",
            "..NNNNNN..OOOOOOOOOOOO..PPPPPP..",
            "..NNNNNN..OOOOOOOOOOOO..PPPPPP..",
            "..NNNNNN................PPPPPP..",
            "................................",
            "................................"),
        new("THE PLAZA", 16, 15,
            "................................",
            "................................",
            "..AAAA..BBBBBB....CCCCCC..DDDD..",
            "..AAAA..BBBBBB....CCCCCC..DDDD..",
            "..AAAA..BBBBBB....CCCCCC..DDDD..",
            "................................",
            "................................",
            "..EEEE..FF..............GG..HH..",
            "..EEEE..FF..............GG..HH..",
            "..EEEE..........II..........HH..",
            "..EEEE.........IIII.........HH..",
            "..EEEE..........II..........HH..",
            "..EEEE..JJ..............KK..HH..",
            "..EEEE..JJ..............KK..HH..",
            "................................",
            "................................",
            "..LLLL..MMMMMM....NNNNNN..OOOO..",
            "..LLLL..MMMMMM....NNNNNN..OOOO..",
            "..LLLL..MMMMMM....NNNNNN..OOOO..",
            "................................",
            "................................"),
    ];

    public static City For(int wave) => All[(wave - 1) % All.Length];
}

/// <summary>The player's jeep: steered and driven like a car, with a gun on top that swivels on its own.</summary>
public sealed class Jeep
{
    public double X, Y, Angle = -Math.PI / 2, Speed;
    /// <summary>The gun's angle, relative to the way the jeep points.</summary>
    public double Gun;
    public bool Alive = true;
    public double Safe, FireIn;
    public const double Radius = 11;
    /// <summary>Which way the gun points, on the screen.</summary>
    public double Aim => Angle + Gun;
}

public sealed class Shell
{
    public double X, Y, Vx, Vy, Life;
    /// <summary>Fired by the enemy, at the jeep.</summary>
    public bool Hostile;
    /// <summary>A rocket from the gunship: it flies over the rooftops.</summary>
    public bool Rocket;
}

/// <summary>
/// An enemy tank: it drives the streets cell by cell, hunting the jeep, turns its turret slowly onto it, and fires
/// when it has a clear shot. Two hits: the first wrecks its engine and slows it, the second finishes it.
/// </summary>
public sealed class Tank
{
    public double X, Y, Angle, Turret;
    /// <summary>The cell it is driving to.</summary>
    public int ToCol, ToRow;
    public int Hp = 2;
    public double FireIn, Entering, Hit;
    public bool Dead;
    public const double Radius = 15;
    public bool Damaged => Hp < 2;
}

/// <summary>The gunship: a helicopter that flies over the buildings, circling the jeep and firing rockets.</summary>
public sealed class Gunship
{
    public double X, Y, Vx, Vy, Angle, T, FireIn = 2, Orbit = 1;
    /// <summary>Rockets left in this burst.</summary>
    public int Burst;
    public bool Leaving;
    public const double Radius = 20;
    public const int Points = 1000;
}

/// <summary>A tank's burnt-out hull, smoking for a while.</summary>
public sealed class Wreck
{
    public double X, Y, Angle, Age;
    public const double Life = 5;
}

/// <summary>A broken-off line, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word, Smoke }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
