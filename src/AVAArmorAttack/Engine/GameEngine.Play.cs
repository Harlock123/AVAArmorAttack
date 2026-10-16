namespace AVAArmorAttack.Engine;

public sealed partial class GameEngine
{
    public const double TurnRate = 3.0, Accel = 520, Brake = 900, Coast = 380, MaxSpeed = 220, ReverseSpeed = 110, SwivelRate = 3.4;
    public const double ShellSpeed = 600, ShellLife = 0.9, FireGap = 0.28;
    public const int MaxShells = 2;
    public const double TankHold = 300, TankRange = 460, TankEvery = 3, RocketSpeed = 360;
    public const double StartSeconds = 2.2, DyingSeconds = 2.4, SafeSeconds = 2.5, ClearSeconds = 3;
    public const int ExtraLifeEvery = 10000, DamagePoints = 100, KillPoints = 400;

    /// <summary>The wave: 1 for the first. Each wave is a district of the city, in turn.</summary>
    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public City City { get; private set; } = Cities.All[0];
    public Jeep Jeep { get; } = new();
    public List<Shell> Shells { get; } = [];
    public List<Tank> Tanks { get; } = [];
    public Gunship? Gunship { get; private set; }
    public List<Wreck> Wrecks { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public JeepPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public int Bonus { get; private set; }
    /// <summary>Tanks still to come into this district.</summary>
    public int TanksToCome { get; private set; }
    /// <summary>How far along the streets every cell is from the jeep (the tanks hunt by it).</summary>
    public int[,] ToJeep { get; private set; } = new int[Field.Cols, Field.Rows];
    /// <summary>For tests: the tanks and the gunship stay put and hold their fire, and no more come in.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _pathCol = -99, _pathRow = -99;
    private bool _held;
    private double _tankIn, _gunshipIn, _ricochetSound, _gunshipTime;

    public int TanksPerWave => Math.Min(16, 5 + 2 * (Wave - 1));
    public int MaxTanks => Math.Min(5, 2 + (Wave - 1) / 2);
    public double TankSpeed => Math.Min(90, 52 + 5 * (Wave - 1));
    public double TurretRate => Math.Min(1.7, 0.9 + 0.08 * (Wave - 1));
    public double TankFireGap => Math.Max(1.2, 2.8 - 0.2 * (Wave - 1));
    public double TankShellSpeed => Math.Min(430, 290 + 15 * (Wave - 1));
    /// <summary>How much the tanks allow for the jeep's movement when they aim: not at all to begin with.</summary>
    public double TankLead => Math.Clamp((Wave - 2) * 0.2, 0, 0.8);
    public double GunshipEvery => Math.Max(14, 32 - 3 * (Wave - 1));

    private void NewGame()
    {
        _rng = new Random(_seed);
        _held = false;
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = ExtraLifeEvery;
        Wreckage.Clear();
        Wrecks.Clear();
        Tanks.Clear();
        Shells.Clear();
        Gunship = null;
        City = Cities.For(1);
        PlaceJeep(City.StartX, City.StartY);
        SetPhase(Phase.Start);
    }

    private void PlaceJeep(double x, double y)
    {
        var j = Jeep;
        j.X = x;
        j.Y = y;
        j.Speed = 0;
        j.Angle = -Math.PI / 2;
        j.Gun = 0;
        j.Alive = true;
        j.Safe = SafeSeconds;
        j.FireIn = 0;
        _pathCol = -99;
    }

    /// <summary>A district: the jeep at its start, and the tanks to come.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        City = Cities.For(wave);
        Tanks.Clear();
        Shells.Clear();
        Wrecks.Clear();
        if (Gunship != null) Music(false);
        Gunship = null;
        TanksToCome = TanksPerWave;
        _tankIn = 1;
        _gunshipIn = wave >= 2 ? GunshipEvery * 0.6 : double.MaxValue;
        PlaceJeep(City.StartX, City.StartY);
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", City.Name, StartSeconds + 0.3);
        Play(Sfx.WaveStart);
    }

    private void Simulate(double dt, Controls c)
    {
        MoveDebris(dt);
        foreach (var w in Wrecks) w.Age += dt;
        Wrecks.RemoveAll(w => w.Age >= Wreck.Life);
        switch (Phase)
        {
            case Phase.Start:
                Drive(dt, c);
                if (PhaseTime >= StartSeconds) SetPhase(Phase.Play);
                break;
            case Phase.Play:
                Drive(dt, c);
                Fire(c);
                Paths();
                Reinforce(dt);
                MoveTanks(dt);
                MoveGunship(dt);
                MoveShells(dt);
                Crashes();
                if (Phase == Phase.Play && TanksToCome == 0 && Tanks.Count == 0) Clear();
                break;
            case Phase.Dying:
                Paths();
                MoveTanks(dt);
                MoveGunship(dt);
                MoveShells(dt);
                if (PhaseTime >= DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                    SetPhase(Phase.Play);
                }
                break;
            case Phase.Clear:
                Drive(dt, c);
                MoveShells(dt);
                MoveGunship(dt);
                if (PhaseTime >= 1 && PhaseTime - dt < 1 && Bonus > 0)
                {
                    Score += Bonus;
                    CheckExtra();
                }
                if (PhaseTime >= ClearSeconds) BeginWave(Wave + 1);
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the jeep ─────────────────────────────

    /// <summary>One step of a jeep's driving: steering, speed, the gun, and the buildings in the way.</summary>
    public static void Move(City city, Jeep j, Controls c, double dt)
    {
        j.Angle = Math.IEEERemainder(j.Angle + c.Turn * TurnRate * dt, Math.PI * 2);
        j.Gun = Math.IEEERemainder(j.Gun + c.Swivel * SwivelRate * dt, Math.PI * 2);
        if (c.Drive > 0) j.Speed = Math.Min(MaxSpeed, j.Speed + (j.Speed < 0 ? Brake : Accel) * dt);
        else if (c.Drive < 0) j.Speed = Math.Max(-ReverseSpeed, j.Speed - (j.Speed > 0 ? Brake : Accel) * dt);
        else j.Speed = j.Speed > 0 ? Math.Max(0, j.Speed - Coast * dt) : Math.Min(0, j.Speed + Coast * dt);
        double x = j.X + Math.Cos(j.Angle) * j.Speed * dt, y = j.Y + Math.Sin(j.Angle) * j.Speed * dt;
        // Scraping along a wall slows it down.
        if (city.Push(ref x, ref y, Jeep.Radius)) j.Speed *= 0.96;
        j.X = x;
        j.Y = y;
    }

    private void Drive(double dt, Controls c)
    {
        var j = Jeep;
        if (!j.Alive) return;
        j.Safe = Math.Max(0, j.Safe - dt);
        j.FireIn -= dt;
        Move(City, j, c, dt);
    }

    private void Fire(Controls c)
    {
        var j = Jeep;
        if (!c.Fire || j.FireIn > 0 || !j.Alive) return;
        if (Shells.Count(s => !s.Hostile) >= MaxShells) return;
        j.FireIn = FireGap;
        double fx = Math.Cos(j.Aim), fy = Math.Sin(j.Aim);
        Shells.Add(new Shell { X = j.X + fx * 14, Y = j.Y + fy * 14, Vx = fx * ShellSpeed, Vy = fy * ShellSpeed, Life = ShellLife });
        Play(Sfx.Fire);
    }

    /// <summary>
    /// Back on the streets after a hit: at the street corner furthest from the enemy (and from the gunship's
    /// rockets), so it isn't shot again straight away.
    /// </summary>
    private void Respawn()
    {
        double bx = City.StartX, by = City.StartY, best = double.MinValue;
        for (int c = 1; c < Field.Cols; c++)
        for (int r = 1; r < Field.Rows; r++)
        {
            if (City.Solid(c, r) || City.Solid(c - 1, r) || City.Solid(c, r - 1) || City.Solid(c - 1, r - 1)) continue;
            double x = c * Field.Cell, y = Field.Top + r * Field.Cell, near = 2000;
            foreach (var t in Tanks) near = Math.Min(near, Field.Distance(x, y, t.X, t.Y) * (City.Clear(x, y, t.X, t.Y) ? 0.6 : 1));
            if (Gunship is { } u) near = Math.Min(near, Field.Distance(x, y, u.X, u.Y));
            // Not out at the very edge, where the tanks come in.
            double score = Math.Min(near, 420) - Field.Distance(x, y, Field.Width / 2, Field.Top + Field.Rows * Field.Cell / 2) * 0.05
                - (c < 3 || c > Field.Cols - 3 ? 200 : 0);
            if (score > best) { best = score; bx = x; by = y; }
        }
        PlaceJeep(bx, by);
        Shells.RemoveAll(s => s.Hostile);
    }

    // ───────────────────────────── the tanks ─────────────────────────────

    /// <summary>How far every cell is from the jeep, worked out again whenever it moves to another cell.</summary>
    private void Paths()
    {
        int c = Field.ColOf(Jeep.X), r = Field.RowOf(Jeep.Y);
        if (c == _pathCol && r == _pathRow) return;
        _pathCol = c;
        _pathRow = r;
        ToJeep = City.Distances(c, r);
    }

    /// <summary>Tanks roll in from the sides, a few at a time; the gunship comes now and then.</summary>
    private void Reinforce(double dt)
    {
        if (Calm) return;
        if (TanksToCome > 0 && !_held && Tanks.Count < MaxTanks && (_tankIn -= dt) <= 0)
        {
            _tankIn = TankEvery;
            // A street at a side, away from the jeep and from any tank already coming in.
            var ends = new List<(int Col, int Row)>();
            foreach (int col in new[] { 0, Field.Cols - 1 })
                for (int row = 0; row < Field.Rows; row++)
                    if (!City.Solid(col, row) && Field.Distance(Field.CellX(col), Field.CellY(row), Jeep.X, Jeep.Y) > 280
                        && !Tanks.Any(t => Field.Distance(t.X, t.Y, Field.CellX(col), Field.CellY(row)) < 80))
                        ends.Add((col, row));
            if (ends.Count == 0) return;
            var (c, r) = ends[_rng.Next(ends.Count)];
            bool left = c == 0;
            Tanks.Add(new Tank
            {
                X = left ? -Field.Cell / 2 : Field.Width + Field.Cell / 2, Y = Field.CellY(r), Angle = left ? 0 : Math.PI, Turret = left ? 0 : Math.PI,
                ToCol = c, ToRow = r, Entering = 1, FireIn = TankFireGap * (0.6 + _rng.NextDouble() * 0.6),
            });
            TanksToCome--;
            Play(Sfx.Rumble);
        }
        if (Gunship == null && Phase == Phase.Play && (_gunshipIn -= dt) <= 0)
        {
            _gunshipIn = GunshipEvery;
            int side = _rng.Next(4);
            double x = side switch { 0 => -30, 1 => Field.Width + 30, _ => 100 + _rng.NextDouble() * (Field.Width - 200) };
            double y = side switch { 2 => Field.Top - 30, 3 => Field.Height + 30, _ => Field.Top + 80 + _rng.NextDouble() * (Field.Bottom - Field.Top - 160) };
            Gunship = new Gunship { X = x, Y = y, Orbit = _rng.Next(2) == 0 ? 1 : -1, FireIn = 2.5 };
            _gunshipTime = 0;
            Music(true);
        }
    }

    /// <summary>Whether a tank has the jeep in its sights: in range, with nothing in the way.</summary>
    private bool Sees(Tank t) =>
        Jeep.Alive && t.Entering <= 0 && Field.Distance(t.X, t.Y, Jeep.X, Jeep.Y) < TankRange && City.Clear(t.X, t.Y, Jeep.X, Jeep.Y);

    private void MoveTanks(double dt)
    {
        foreach (var t in Tanks)
        {
            t.Hit = Math.Max(0, t.Hit - dt);
            if (Calm) continue;
            bool sees = Sees(t);
            double tx = Field.CellX(t.ToCol), ty = Field.CellY(t.ToRow);
            double dx = tx - t.X, dy = ty - t.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1)
            {
                t.X = tx;
                t.Y = ty;
                if (t.Entering > 0) t.Entering = 0;
                // Close enough with a clear shot: it stops to fire. Otherwise on towards the jeep.
                if (!(sees && Field.Distance(t.X, t.Y, Jeep.X, Jeep.Y) < TankHold)) Next(t);
            }
            else
            {
                // Turn to face the way, then roll.
                double want = Math.Atan2(dy, dx), diff = Math.IEEERemainder(want - t.Angle, Math.PI * 2);
                t.Angle += Math.Clamp(diff, -3 * dt, 3 * dt);
                if (Math.Abs(diff) < 0.15)
                {
                    double step = Math.Min(d, TankSpeed * (t.Damaged ? 0.5 : 1) * dt);
                    t.X += dx / d * step;
                    t.Y += dy / d * step;
                }
            }
            if (t.Entering > 0) continue;
            // The turret follows the jeep whenever it's near, seen or not (it hears the engine), so turning a
            // corner onto a tank means its gun is already on you; aiming where the jeep is going, later on.
            double aim = t.Angle;
            if (Jeep.Alive && Field.Distance(t.X, t.Y, Jeep.X, Jeep.Y) < TankRange)
            {
                double lead = TankLead * Field.Distance(t.X, t.Y, Jeep.X, Jeep.Y) / TankShellSpeed;
                aim = Math.Atan2(Jeep.Y + Math.Sin(Jeep.Angle) * Jeep.Speed * lead - t.Y, Jeep.X + Math.Cos(Jeep.Angle) * Jeep.Speed * lead - t.X);
            }
            double off = Math.IEEERemainder(aim - t.Turret, Math.PI * 2);
            t.Turret += Math.Clamp(off, -TurretRate * dt, TurretRate * dt);
            t.FireIn -= dt;
            if (sees && Math.Abs(off) < 0.06 && t.FireIn <= 0 && Jeep.Safe <= 0 && Phase == Phase.Play)
            {
                t.FireIn = TankFireGap * (t.Damaged ? 1.4 : 1);
                double fx = Math.Cos(t.Turret), fy = Math.Sin(t.Turret);
                Shells.Add(new Shell { X = t.X + fx * 22, Y = t.Y + fy * 22, Vx = fx * TankShellSpeed, Vy = fy * TankShellSpeed, Life = 2, Hostile = true });
                Play(Sfx.TankFire);
            }
        }
    }

    /// <summary>
    /// A tank's next cell: the neighbour nearer the jeep along the streets (or, with the jeep gone, anywhere but
    /// back), so long as no other tank is in it or heading for it.
    /// </summary>
    private void Next(Tank t)
    {
        int c = t.ToCol, r = t.ToRow;
        int back = Math.Abs(Math.Cos(t.Angle)) > 0.5 ? (Math.Cos(t.Angle) > 0 ? 1 : 0) : (Math.Sin(t.Angle) > 0 ? 3 : 2);
        (int, int)? best = null;
        double bestScore = double.MaxValue;
        for (int i = 0; i < 4; i++)
        {
            var (dc, dr) = City.Steps[i];
            int nc = c + dc, nr = r + dr;
            if (City.Solid(nc, nr) || Taken(t, nc, nr)) continue;
            double score;
            if (Jeep.Alive && ToJeep[nc, nr] >= 0) score = ToJeep[nc, nr] + _rng.NextDouble() * 0.5;
            else score = _rng.NextDouble() + (i == (back ^ 1) ? 5 : 0);
            // Going straight on is a little better than turning.
            if (Math.Abs(Math.Atan2(dr, dc) - t.Angle) < 0.1) score -= 0.3;
            if (score < bestScore) { bestScore = score; best = (nc, nr); }
        }
        if (best is { } b && (!Jeep.Alive || ToJeep[b.Item1, b.Item2] < 0 || ToJeep[b.Item1, b.Item2] <= Math.Max(0, ToJeep[c, r]) || _rng.Next(60) == 0))
            (t.ToCol, t.ToRow) = b;
    }

    private bool Taken(Tank me, int col, int row) =>
        Tanks.Any(o => o != me && ((o.ToCol == col && o.ToRow == row) || (Field.ColOf(o.X) == col && Field.RowOf(o.Y) == row)
            || Field.Distance(o.X, o.Y, Field.CellX(col), Field.CellY(row)) < Tank.Radius * 2 + 2));

    // ───────────────────────────── the gunship ─────────────────────────────

    private void MoveGunship(double dt)
    {
        if (Gunship is not { } u) return;
        u.T += dt;
        _gunshipTime += dt;
        if (Calm) return;
        if (_gunshipTime > 24 || Phase == Phase.Clear) u.Leaving = true;
        double speed = 165 + 10 * Wave, wx, wy;
        if (u.Leaving || !Jeep.Alive)
        {
            // Away the nearest way off the screen.
            wx = u.X < Field.Width / 2 ? -200 : Field.Width + 200;
            wy = u.Y;
        }
        else
        {
            // Circles the jeep, a little ahead of where it is round the circle.
            double a = Math.Atan2(u.Y - Jeep.Y, u.X - Jeep.X) + u.Orbit * 0.7;
            wx = Jeep.X + Math.Cos(a) * 190;
            wy = Math.Clamp(Jeep.Y + Math.Sin(a) * 190, Field.Top + 20, Field.Bottom - 20);
        }
        double dx = wx - u.X, dy = wy - u.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        u.Vx += (dx / d * speed - u.Vx) * 2.2 * dt;
        u.Vy += (dy / d * speed - u.Vy) * 2.2 * dt;
        u.X += u.Vx * dt;
        u.Y += u.Vy * dt;
        if (Math.Abs(u.Vx) + Math.Abs(u.Vy) > 5) u.Angle = Math.Atan2(u.Vy, u.Vx);
        if (u.Leaving && (u.X < -60 || u.X > Field.Width + 60))
        {
            Gunship = null;
            Music(false);
            return;
        }
        if (u.Leaving || !Jeep.Alive || Phase != Phase.Play) return;
        u.FireIn -= dt;
        if (u.FireIn <= 0 && u.Burst == 0 && Field.Distance(u.X, u.Y, Jeep.X, Jeep.Y) < 380 && Jeep.Safe <= 0)
        {
            u.Burst = 3;
            u.FireIn = 0;
        }
        if (u.Burst > 0 && u.FireIn <= 0)
        {
            u.Burst--;
            u.FireIn = u.Burst > 0 ? 0.2 : Math.Max(1.8, 3.4 - 0.15 * Wave);
            double a = Math.Atan2(Jeep.Y - u.Y, Jeep.X - u.X) + (_rng.NextDouble() - 0.5) * 0.25;
            Shells.Add(new Shell { X = u.X, Y = u.Y, Vx = Math.Cos(a) * RocketSpeed, Vy = Math.Sin(a) * RocketSpeed, Life = 1.5, Hostile = true, Rocket = true });
            Play(Sfx.Rocket);
        }
    }

    // ───────────────────────────── shells, and what they hit ─────────────────────────────

    private void MoveShells(double dt)
    {
        _ricochetSound -= dt;
        foreach (var s in Shells)
        {
            // In small steps, so a shell can't jump a wall or a tank.
            for (int k = 0; k < 3 && s.Life > 0; k++)
            {
                s.X += s.Vx * dt / 3;
                s.Y += s.Vy * dt / 3;
                if (s.X < -20 || s.X > Field.Width + 20 || s.Y < Field.Top - 20 || s.Y > Field.Bottom + 20) { s.Life = 0; break; }
                if (!s.Rocket && City.SolidAt(s.X, s.Y))
                {
                    s.Life = 0;
                    Sparks(s.X - s.Vx * dt / 3, s.Y - s.Vy * dt / 3, s.Hostile ? 0xFFFF8A4A : 0xFFFFF4C0, 4, 0.35);
                    if (_ricochetSound <= 0) { _ricochetSound = 0.1; Play(Sfx.Ricochet); }
                    break;
                }
                if (s.Hostile) HitJeep(s);
                else HitEnemy(s);
            }
            s.Life -= dt;
        }
        // A shot meeting a shell head-on: both go.
        foreach (var a in Shells.Where(s => !s.Hostile && s.Life > 0))
        foreach (var b in Shells.Where(s => s.Hostile && s.Life > 0 && !s.Rocket))
            if (Field.Distance(a.X, a.Y, b.X, b.Y) < 9)
            {
                a.Life = b.Life = 0;
                Sparks(a.X, a.Y, 0xFFFFF4C0, 6, 0.4);
                Play(Sfx.Ricochet);
            }
        Shells.RemoveAll(s => s.Life <= 0);
        Tanks.RemoveAll(t => t.Dead);
    }

    private void HitEnemy(Shell s)
    {
        foreach (var t in Tanks)
        {
            if (t.Dead || Field.Distance(s.X, s.Y, t.X, t.Y) > Tank.Radius + 2) continue;
            s.Life = 0;
            t.Hp--;
            t.Hit = 0.25;
            if (t.Hp > 0)
            {
                Award(DamagePoints, t.X, t.Y);
                Sparks(s.X, s.Y, 0xFFFFE04A, 8, 0.6);
                Play(Sfx.Clang);
                return;
            }
            t.Dead = true;
            Award(KillPoints, t.X, t.Y);
            Sparks(t.X, t.Y, TankColour, 14, 1.2);
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = t.X, Y = t.Y, Life = 0.6, Size = 1.8, Colour = 0xFFFFB04A });
            Wrecks.Add(new Wreck { X = t.X, Y = t.Y, Angle = t.Angle });
            Play(Sfx.TankBlast);
            return;
        }
        if (Gunship is { } u && Field.Distance(s.X, s.Y, u.X, u.Y) < Gunship.Radius)
        {
            s.Life = 0;
            Award(Gunship.Points, u.X, u.Y);
            Sparks(u.X, u.Y, GunshipColour, 16, 1.4);
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = u.X, Y = u.Y, Life = 0.7, Size = 2.2, Colour = 0xFFFFE04A });
            Play(Sfx.GunshipDown);
            Gunship = null;
            Music(false);
        }
    }

    private void HitJeep(Shell s)
    {
        var j = Jeep;
        if (!j.Alive || Phase != Phase.Play || Field.Distance(s.X, s.Y, j.X, j.Y) > Jeep.Radius + 2) return;
        s.Life = 0;
        if (j.Safe > 0) return;
        Kill(s.Rocket ? "gunship" : "tank shell");
    }

    /// <summary>The jeep run into, or run over by, a tank.</summary>
    private void Crashes()
    {
        var j = Jeep;
        if (!j.Alive || Phase != Phase.Play) return;
        foreach (var t in Tanks)
        {
            if (Field.Distance(j.X, j.Y, t.X, t.Y) > Tank.Radius + Jeep.Radius - 2) continue;
            if (j.Safe > 0)
            {
                // Can't be hurt just yet: shoved aside instead.
                double dx = j.X - t.X, dy = j.Y - t.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                double x = t.X + dx / d * (Tank.Radius + Jeep.Radius), y = t.Y + dy / d * (Tank.Radius + Jeep.Radius);
                City.Push(ref x, ref y, Jeep.Radius);
                j.X = x;
                j.Y = y;
                continue;
            }
            Kill("crushed");
            return;
        }
    }

    private void Kill(string cause)
    {
        var j = Jeep;
        Cause = cause;
        j.Alive = false;
        j.Speed = 0;
        Sparks(j.X, j.Y, JeepColour, 16, 1.6);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = j.X, Y = j.Y, Life = 0.7, Size = 2, Colour = 0xFFFFFFFF });
        Lives--;
        Play(Sfx.Death);
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds);
    }

    public const uint JeepColour = 0xFF7AFF8A, TankColour = 0xFF6AE0FF, GunshipColour = 0xFFFF6AD0;

    private void Clear()
    {
        Bonus = 1000 * Math.Min(Wave, 5);
        Shells.RemoveAll(s => s.Hostile);
        Play(Sfx.WaveClear);
        Say($"{City.Name} IS CLEAR", $"BONUS {Bonus}", ClearSeconds);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y)
    {
        Score += points;
        Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y - 14, Text = points.ToString(), Life = 0.7, Colour = 0xFFFFE14A });
        CheckExtra();
    }

    private void CheckExtra()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Sparks(double x, double y, uint colour, int n, double life)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 170;
            Wreckage.Add(new Debris
            {
                X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Angle = _rng.NextDouble() * 6, Spin = (_rng.NextDouble() - 0.5) * 12,
                Length = 4 + _rng.NextDouble() * (n > 8 ? 12 : 5), Life = life * (0.5 + _rng.NextDouble() * 0.5), Colour = colour,
            });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a wave, past its name.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Jeep.Safe = 0;
        Message = SubMessage = null;
    }

    /// <summary>A tank standing in a cell.</summary>
    internal Tank PutTank(int col, int row, double angle = 0)
    {
        var t = new Tank { X = Field.CellX(col), Y = Field.CellY(row), ToCol = col, ToRow = row, Angle = angle, Turret = angle, FireIn = 0 };
        Tanks.Add(t);
        return t;
    }

    internal Gunship PutGunship(double x, double y)
    {
        Gunship = new Gunship { X = x, Y = y };
        _gunshipTime = 0;
        return Gunship;
    }

    internal void PutJeep(double x, double y, double angle = -Math.PI / 2)
    {
        PlaceJeep(x, y);
        Jeep.Angle = angle;
        Jeep.Safe = 0;
    }

    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
    internal void NoMoreTanks() => TanksToCome = 0;
    /// <summary>No more tanks come in for now, though there are still some to come.</summary>
    internal void HoldTanks() => _held = true;
}
