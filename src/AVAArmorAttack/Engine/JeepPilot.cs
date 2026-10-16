namespace AVAArmorAttack.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It picks the enemy it can get a clear shot at
/// soonest, finds the nearest street corner with that shot (by the streets, keeping clear of the tanks), and drives
/// there. All the while it keeps its gun on whatever it can see, leading it, and fires when the shot is clear. When a
/// shell is coming at it, it tries a few ways of driving and takes the one that gets it furthest out of the way.
/// </summary>
public sealed class JeepPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    /// <summary>It thinks again about where to go only every so often.</summary>
    public const int LookEvery = 10;
    /// <summary>The furthest it likes to shoot from.</summary>
    public const double Range = 380;

    private int _tick, _stuck, _backing, _goalCol = -1, _goalRow = -1;
    private object? _target;
    private int[,]? _toGoal;
    private double _lastX, _lastY;
    private Controls _dodge;
    private int _dodging;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var j = g.Jeep;
        if (g.Phase is not (Phase.Play or Phase.Start) || !j.Alive) { Doing = "waiting"; _target = null; return default; }
        var city = g.City;

        // Something about to hit it: out of the way first.
        if (_dodging > 0) _dodging--;
        else if (_tick % 6 == 0 && Threat(g, j, default) is { } miss && miss < Jeep.Radius + 6) { _dodge = Dodge(g); _dodging = 12; }

        if (_tick % LookEvery == 0 || !Exists(g, _target)) Choose(g);
        var (gx, gy) = Gun(g);
        int turn = 0, drive = 0;
        if (_dodging > 0) { turn = _dodge.Turn; drive = _dodge.Drive; Doing = "dodging"; }
        else if (_backing > 0)
        {
            // Stuck on a corner: back off, turning.
            _backing--;
            turn = 1;
            drive = -1;
            Doing = "backing off";
        }
        else if (_toGoal != null && _goalCol >= 0)
        {
            int c = Field.ColOf(j.X), r = Field.RowOf(j.Y);
            if (c == _goalCol && r == _goalRow && Math.Abs(j.X - Field.CellX(c)) < 10 && Math.Abs(j.Y - Field.CellY(r)) < 10)
                Doing = "in position";
            else
            {
                var (wx, wy) = Waypoint(city, j);
                double want = Math.Atan2(wy - j.Y, wx - j.X), diff = Math.IEEERemainder(want - j.Angle, Math.PI * 2);
                turn = Math.Abs(diff) < 0.08 ? 0 : Math.Sign(diff);
                double left = Field.Distance(j.X, j.Y, Field.CellX(_goalCol), Field.CellY(_goalRow));
                drive = Math.Abs(diff) < 0.7 ? 1 : 0;
                // Easing in to the corner, so it doesn't overshoot.
                if (left < 40 && j.Speed > 90) drive = 0;
                Doing = "driving";
            }
        }
        else Doing = "looking";

        // Stuck against something while trying to drive.
        if (drive > 0 && Field.Distance(j.X, j.Y, _lastX, _lastY) < 0.4) { if (++_stuck > 30) { _stuck = 0; _backing = 25; } }
        else _stuck = 0;
        _lastX = j.X;
        _lastY = j.Y;

        // The gun: allowing for the way the jeep is turning.
        double aim = Math.Atan2(gy - j.Y, gx - j.X), off = Math.IEEERemainder(aim - j.Aim - turn * GameEngine.TurnRate * Field.Step, Math.PI * 2);
        int swivel = Math.Abs(off) < 0.03 ? 0 : Math.Sign(off);
        double dist = Field.Distance(j.X, j.Y, gx, gy);
        bool fire = _target != null && Math.Abs(off) < Math.Atan2(Tank.Radius * 0.7, Math.Max(dist, 1)) + 0.02
            && dist < GameEngine.ShellSpeed * GameEngine.ShellLife * 0.95 && Clear(g, j, gx, gy);
        if (fire && Doing != "dodging") Doing = "firing";
        return new Controls(turn, drive, swivel, fire);
    }

    private static bool Exists(GameEngine g, object? t) => t switch
    {
        Tank k => !k.Dead && g.Tanks.Contains(k),
        Gunship u => g.Gunship == u,
        _ => false,
    };

    private static (double X, double Y, double Vx, double Vy) Where(GameEngine g, object t)
    {
        switch (t)
        {
            case Tank k:
            {
                double tx = Field.CellX(k.ToCol) - k.X, ty = Field.CellY(k.ToRow) - k.Y, d = Math.Sqrt(tx * tx + ty * ty);
                double v = d < 1 ? 0 : g.TankSpeed * (k.Damaged ? 0.5 : 1);
                return (k.X, k.Y, d < 1 ? 0 : tx / d * v, d < 1 ? 0 : ty / d * v);
            }
            case Gunship u: return (u.X, u.Y, u.Vx, u.Vy);
            default: return default;
        }
    }

    private static bool Clear(GameEngine g, Jeep j, double x, double y)
    {
        double a = Math.Atan2(y - j.Y, x - j.X);
        return g.City.Clear(j.X + Math.Cos(a) * 14, j.Y + Math.Sin(a) * 14, x, y);
    }

    /// <summary>Where the gun should point: at the target if it can see it, else whatever it can see that's nearest, else where it's going.</summary>
    private (double X, double Y) Gun(GameEngine g)
    {
        var j = g.Jeep;
        IEnumerable<object> all = g.Tanks.Where(t => t.Entering <= 0);
        if (g.Gunship != null) all = all.Append(g.Gunship);
        var seen = all.Where(t => { var (x, y, _, _) = Where(g, t); return Clear(g, j, x, y); })
            .OrderBy(t => (t == _target ? 0.6 : 1) * Field.Distance(j.X, j.Y, Where(g, t).X, Where(g, t).Y)).FirstOrDefault();
        var t = seen ?? _target;
        if (t == null) return (j.X + Math.Cos(j.Angle) * 100, j.Y + Math.Sin(j.Angle) * 100);
        _target = t;
        var (tx, ty, vx, vy) = Where(g, t);
        // Where it will be when a shot gets there.
        double time = Field.Distance(j.X, j.Y, tx, ty) / GameEngine.ShellSpeed;
        for (int i = 0; i < 2; i++) time = Field.Distance(j.X, j.Y, tx + vx * time, ty + vy * time) / GameEngine.ShellSpeed;
        return (tx + vx * time, ty + vy * time);
    }

    /// <summary>
    /// The enemy it can get a shot at soonest, and the cell to shoot from: the nearest by the streets with a clear
    /// line to it, not too close to a tank. The gunship counts for more, and so does a tank already damaged.
    /// </summary>
    private void Choose(GameEngine g)
    {
        var j = g.Jeep;
        var city = g.City;
        int jc = Field.ColOf(j.X), jr = Field.RowOf(j.Y);
        bool Near(int c, int r) => g.Tanks.Any(t => Field.Distance(Field.CellX(c), Field.CellY(r), t.X, t.Y) < 70
            || Field.Distance(Field.CellX(c), Field.CellY(r), Field.CellX(t.ToCol), Field.CellY(t.ToRow)) < 60);
        var paths = city.Distances(jc, jr, (c, r) => Near(c, r) && Math.Abs(c - jc) + Math.Abs(r - jr) > 1);
        IEnumerable<object> all = g.Tanks.Where(t => t.Entering <= 0);
        if (g.Gunship != null) all = all.Append(g.Gunship);
        double best = double.MaxValue;
        (int, int)? goal = null;
        _target = null;
        foreach (var t in all)
        {
            var (tx, ty, _, _) = Where(g, t);
            double weight = t is Gunship ? 0.7 : t is Tank { Damaged: true } ? 0.8 : 1;
            for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                if (paths[c, r] < 0) continue;
                double x = Field.CellX(c), y = Field.CellY(r), d = Field.Distance(x, y, tx, ty);
                if (d > Range || d < 130) continue;
                double cost = (paths[c, r] + d / Range * 2) * weight;
                if (cost >= best || !city.Clear(x, y, tx, ty)) continue;
                // Not where the other tanks could shoot it as well.
                foreach (var o in g.Tanks)
                    if (o != t && o.Entering <= 0 && Field.Distance(x, y, o.X, o.Y) < GameEngine.TankRange && city.Clear(x, y, o.X, o.Y)) cost += 3;
                if (cost < best) { best = cost; goal = (c, r); _target = t; }
            }
        }
        if (goal is not { } gcell)
        {
            // Nothing to be had: wait in the middle of the district, away from the edges.
            _goalCol = -1;
            _toGoal = null;
            return;
        }
        if (gcell != (_goalCol, _goalRow) || _toGoal == null)
        {
            (_goalCol, _goalRow) = gcell;
            _toGoal = city.Distances(_goalCol, _goalRow, (c, r) => Near(c, r) && Math.Abs(c - jc) + Math.Abs(r - jr) > 1);
        }
    }

    /// <summary>The furthest cell along the way to the goal that it can drive straight to.</summary>
    private (double X, double Y) Waypoint(City city, Jeep j)
    {
        int c = Field.ColOf(j.X), r = Field.RowOf(j.Y);
        var d = _toGoal!;
        if (c < 0 || r < 0 || c >= Field.Cols || r >= Field.Rows || d[c, r] < 0) return (Field.CellX(_goalCol), Field.CellY(_goalRow));
        var (wx, wy) = (Field.CellX(c), Field.CellY(r));
        for (int step = 0; step < 6 && d[c, r] > 0; step++)
        {
            (int, int)? next = null;
            foreach (var (dc, dr) in City.Steps)
            {
                int nc = c + dc, nr = r + dr;
                if (nc >= 0 && nr >= 0 && nc < Field.Cols && nr < Field.Rows && d[nc, nr] >= 0 && d[nc, nr] < d[c, r]) { next = (nc, nr); break; }
            }
            if (next is not { } n) break;
            (c, r) = n;
            double x = Field.CellX(c), y = Field.CellY(r);
            // Only as far as a jeep's width can get there in a straight line.
            if (!Wide(city, j.X, j.Y, x, y)) break;
            (wx, wy) = (x, y);
        }
        return (wx, wy);
    }

    private static bool Wide(City city, double ax, double ay, double bx, double by)
    {
        double a = Math.Atan2(by - ay, bx - ax), px = -Math.Sin(a) * (Jeep.Radius + 2), py = Math.Cos(a) * (Jeep.Radius + 2);
        return city.Clear(ax + px, ay + py, bx + px, by + py) && city.Clear(ax - px, ay - py, bx - px, by - py);
    }

    /// <summary>How close the nearest coming shell will pass, driving like this for the next while (null if none is coming).</summary>
    private static double? Threat(GameEngine g, Jeep j, Controls c, bool drive = false)
    {
        var shells = g.Shells.Where(s => s.Hostile).ToList();
        if (shells.Count == 0) return null;
        var me = new Jeep { X = j.X, Y = j.Y, Angle = j.Angle, Speed = j.Speed, Gun = j.Gun };
        var pos = shells.Select(s => (s.X, s.Y)).ToArray();
        double? miss = null;
        for (int t = 0; t < 45; t++)
        {
            if (drive) GameEngine.Move(g.City, me, c, Field.Step);
            for (int i = 0; i < shells.Count; i++)
            {
                var s = shells[i];
                if (double.IsNaN(pos[i].X) || t * Field.Step > s.Life) continue;
                pos[i] = (pos[i].X + s.Vx * Field.Step, pos[i].Y + s.Vy * Field.Step);
                if (!s.Rocket && g.City.SolidAt(pos[i].X, pos[i].Y)) { pos[i] = (double.NaN, double.NaN); continue; }
                double d = Field.Distance(pos[i].X, pos[i].Y, me.X, me.Y);
                if (miss is not { } m || d < m) miss = d;
            }
        }
        return miss;
    }

    /// <summary>The way of driving, of a handful, that takes it furthest from the shells' paths.</summary>
    private static Controls Dodge(GameEngine g)
    {
        Controls best = default;
        double most = double.MinValue;
        foreach (int drive in new[] { 1, -1 })
        foreach (int turn in new[] { 0, -1, 1 })
        {
            var c = new Controls(turn, drive);
            double miss = Threat(g, g.Jeep, c, drive: true) ?? 999;
            if (miss > most + 0.5) { most = miss; best = c; }
        }
        return best;
    }
}
