using AVAArmorAttack.Engine;

namespace AVAArmorAttack.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaarmorattack-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The Old Town in play, calm: no tanks come in, and any put there stay put and hold their fire.</summary>
    private static GameEngine Game(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        return g;
    }

    /// <summary>A live district where no more tanks come in (though it isn't over): the ones put there move and fire.</summary>
    private static GameEngine Live(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.HoldTanks();
        return g;
    }

    /// <summary>A real game, with plenty of lives.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // In the Old Town, rows 13 and 14 are one long street right across, and the jeep starts in the middle of it.
    private const double StreetY = 80 + 13.5 * 32;

    // ───────────────────────────── the city ─────────────────────────────

    [Fact]
    public void Every_district_is_streets_that_all_join_up_with_a_way_in_at_each_side()
    {
        foreach (var city in Cities.All)
        {
            Assert.Equal(Field.Rows, city.Plan.Length);
            Assert.All(city.Plan, row => Assert.Equal(Field.Cols, row.Length));
            // The sides are open street, where the tanks come in.
            for (int r = 0; r < Field.Rows; r++)
            {
                Assert.False(city.Solid(0, r), city.Name);
                Assert.False(city.Solid(Field.Cols - 1, r), city.Name);
            }
            var d = city.Distances(0, 0);
            for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
                Assert.True(city.Solid(c, r) || d[c, r] >= 0, $"{city.Name}: ({c}, {r}) is cut off");
            // The jeep starts at a corner of four street cells.
            int sc = (int)(city.StartX / Field.Cell), sr = (int)((city.StartY - Field.Top) / Field.Cell);
            Assert.False(city.Solid(sc, sr) || city.Solid(sc - 1, sr) || city.Solid(sc, sr - 1) || city.Solid(sc - 1, sr - 1), city.Name);
        }
        Assert.Equal(3, Cities.All.Select(c => c.Name).Distinct().Count());
    }

    [Fact]
    public void A_line_of_fire_is_clear_down_a_street_and_blocked_by_a_building()
    {
        var city = Cities.All[0];
        Assert.True(city.Clear(20, StreetY, 1000, StreetY));
        // From the middle of the street to the far side of the block above.
        Assert.False(city.Clear(512, 528, Field.CellX(20), Field.CellY(10)));
        Assert.Equal(5, city.Distances(0, 13)[5, 13]);
    }

    // ───────────────────────────── the jeep ─────────────────────────────

    [Fact]
    public void The_jeep_turns_speeds_up_coasts_to_a_stop_and_reverses_slower()
    {
        var g = Game();
        g.PutJeep(900, StreetY, Math.PI);
        g.Run(60, new Controls(Drive: 1));
        Assert.Equal(GameEngine.MaxSpeed, g.Jeep.Speed, 3);
        Assert.True(g.Jeep.X < 860);
        g.Idle(60);
        Assert.Equal(0, g.Jeep.Speed);
        g.Run(60, new Controls(Drive: -1));
        Assert.Equal(-GameEngine.ReverseSpeed, g.Jeep.Speed, 3);
        double a = g.Jeep.Angle;
        g.Run(30, new Controls(Turn: -1));
        Assert.Equal(Math.IEEERemainder(a - GameEngine.TurnRate * 0.5, Math.PI * 2), g.Jeep.Angle, 3);
    }

    [Fact]
    public void Buildings_stop_the_jeep()
    {
        var g = Game();
        // Down from the street, into the building below.
        g.PutJeep(560, StreetY, Math.PI / 2);
        g.Run(90, new Controls(Drive: 1));
        Assert.Equal(80 + 15 * 32 - Jeep.Radius, g.Jeep.Y, 0);
        Assert.True(g.Cause == "" && g.Jeep.Alive);
    }

    [Fact]
    public void The_gun_swings_on_its_own_and_turns_with_the_jeep()
    {
        var g = Game();
        g.Run(30, new Controls(Swivel: 1));
        Assert.Equal(GameEngine.SwivelRate * 0.5, g.Jeep.Gun, 3);
        Assert.Equal(-Math.PI / 2, g.Jeep.Angle, 6);
        double gun = g.Jeep.Gun;
        g.Run(20, new Controls(Turn: 1));
        Assert.Equal(gun, g.Jeep.Gun, 6);
        Assert.Equal(g.Jeep.Angle + gun, g.Jeep.Aim, 6);
        // And it fires where the gun points, not where the jeep does.
        g.Run(1, FireButton);
        var s = Assert.Single(g.Shells);
        Assert.Equal(g.Jeep.Aim, Math.Atan2(s.Vy, s.Vx), 3);
    }

    [Fact]
    public void Two_shots_at_once_at_most_and_a_building_stops_them()
    {
        var g = Game();
        g.PutJeep(512, StreetY, Math.PI);
        int most = 0;
        for (int i = 0; i < 60; i++)
        {
            g.Run(1, FireButton);
            most = Math.Max(most, g.Shells.Count);
        }
        Assert.Equal(GameEngine.MaxShells, most);
        g.Idle(60);
        Assert.Empty(g.Shells);
        g.Wreckage.Clear();
        g.PutJeep(560, StreetY, Math.PI / 2);
        g.Run(1, FireButton);
        g.Idle(8);
        Assert.Empty(g.Shells);
        Assert.NotEmpty(g.Wreckage);
    }

    // ───────────────────────────── the tanks ─────────────────────────────

    [Fact]
    public void A_tank_takes_two_hits_one_to_damage_it_and_one_to_finish_it()
    {
        var g = Game();
        g.PutJeep(512, StreetY, 0);
        var t = g.PutTank(25, 13, Math.PI);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.True(t.Damaged);
        Assert.Equal(GameEngine.DamagePoints, g.Score);
        Assert.Single(g.Tanks);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Empty(g.Tanks);
        Assert.Equal(GameEngine.DamagePoints + GameEngine.KillPoints, g.Score);
        Assert.Single(g.Wrecks);
    }

    [Fact]
    public void A_damaged_tank_drives_at_half_speed()
    {
        double Moved(bool damaged)
        {
            var g = Live();
            g.Jeep.Safe = 100;
            var t = g.PutTank(28, 13, Math.PI);
            if (damaged) t.Hp = 1;
            double x = t.X;
            g.Idle(60);
            return x - t.X;
        }
        Assert.Equal(Moved(false) / 2, Moved(true), 0);
    }

    [Fact]
    public void A_tank_with_a_clear_line_turns_its_turret_and_fires_and_the_shell_kills()
    {
        var g = Live();
        g.PutJeep(512, StreetY);
        g.PutTank(25, 13, Math.PI);
        int ticks = 0;
        while (g.Phase == Phase.Play && ticks++ < 240) g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("tank shell", g.Cause);
        Assert.Equal(2, g.Lives);
    }

    [Fact]
    public void A_tank_follows_the_jeep_with_its_turret_through_a_building_but_holds_its_fire()
    {
        var g = Live();
        g.PutJeep(512, 528);
        var t = g.PutTank(20, 10, 0);
        t.Turret = 2.3;
        g.Idle(20);
        double want = Math.Atan2(g.Jeep.Y - t.Y, g.Jeep.X - t.X);
        Assert.Equal(0, Math.IEEERemainder(t.Turret - want, Math.PI * 2), 1);
        Assert.False(g.City.Clear(t.X, t.Y, g.Jeep.X, g.Jeep.Y));
        Assert.Empty(g.Shells);
    }

    [Fact]
    public void Tanks_hunt_the_jeep_along_the_streets_and_never_share_a_cell()
    {
        var g = Live();
        g.Jeep.Safe = 1000;
        var a = g.PutTank(1, 0);
        var b = g.PutTank(2, 1);
        g.Idle(1);
        int start = g.ToJeep[1, 0];
        for (int i = 0; i < 60 * 8; i++)
        {
            g.Idle(1);
            Assert.True(Field.Distance(a.X, a.Y, b.X, b.Y) >= Tank.Radius * 2 - 1, $"tanks overlap at tick {i}");
        }
        Assert.True(g.ToJeep[Field.ColOf(a.X), Field.RowOf(a.Y)] < start - 6);
        Assert.True(g.ToJeep[Field.ColOf(b.X), Field.RowOf(b.Y)] < start - 6);
    }

    [Fact]
    public void A_tank_runs_over_the_jeep_unless_it_is_safe_and_then_it_is_shoved_aside()
    {
        var g = Live();
        g.PutTank(16, 13).FireIn = 99;
        g.PutJeep(Field.CellX(16) + 20, StreetY);
        g.Jeep.Safe = 1;
        g.Idle(1);
        Assert.True(g.Jeep.Alive);
        Assert.True(Field.Distance(g.Jeep.X, g.Jeep.Y, Field.CellX(16), StreetY) >= Tank.Radius + Jeep.Radius - 1);
        g.PutJeep(Field.CellX(16) + 20, StreetY);
        g.Idle(1);
        Assert.False(g.Jeep.Alive);
        Assert.Equal("crushed", g.Cause);
    }

    [Fact]
    public void Tanks_roll_in_from_the_sides_a_few_at_a_time()
    {
        var g = Real();
        g.Jeep.Safe = 1000;
        int most = 0, came = 0;
        var seen = new HashSet<Tank>();
        for (int i = 0; i < 60 * 40; i++)
        {
            g.Idle(1);
            foreach (var t in g.Tanks.Where(seen.Add))
            {
                came++;
                Assert.True(t.X < 0 || t.X > Field.Width, "comes in from off the side");
            }
            most = Math.Max(most, g.Tanks.Count);
        }
        Assert.Equal(g.MaxTanks, most);
        Assert.Equal(g.TanksPerWave, came + g.TanksToCome);
    }

    [Fact]
    public void Each_wave_brings_more_tanks_faster_and_better_shots()
    {
        var one = Real(1);
        var six = Real(6);
        Assert.True(six.TanksPerWave > one.TanksPerWave);
        Assert.True(six.MaxTanks > one.MaxTanks);
        Assert.True(six.TankSpeed > one.TankSpeed);
        Assert.True(six.TankFireGap < one.TankFireGap);
        Assert.True(six.TankShellSpeed > one.TankShellSpeed);
        Assert.Equal(0, one.TankLead);
        Assert.True(six.TankLead > 0);
    }

    // ───────────────────────────── the gunship ─────────────────────────────

    [Fact]
    public void Rockets_fly_over_the_rooftops_where_tank_shells_stop()
    {
        var g = Live();
        g.PutJeep(512, 528);
        double sx = Field.CellX(20), sy = Field.CellY(10), a = Math.Atan2(528 - sy, 512 - sx);
        g.Shells.Add(new Shell { X = sx, Y = sy, Vx = Math.Cos(a) * 300, Vy = Math.Sin(a) * 300, Life = 2, Hostile = true });
        g.Idle(60);
        Assert.True(g.Jeep.Alive);
        g.Shells.Add(new Shell { X = sx, Y = sy, Vx = Math.Cos(a) * 300, Vy = Math.Sin(a) * 300, Life = 2, Hostile = true, Rocket = true });
        g.Idle(60);
        Assert.False(g.Jeep.Alive);
        Assert.Equal("gunship", g.Cause);
    }

    [Fact]
    public void The_gunship_is_worth_1000_and_flies_off_in_the_end()
    {
        var g = Game();
        g.PutJeep(512, 528);
        g.PutGunship(512, 380);
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Null(g.Gunship);
        Assert.Equal(Gunship.Points, g.Score);
        var h = Live();
        h.Jeep.Safe = 1000;
        h.PutTank(1, 0);
        h.PutGunship(300, 300);
        int ticks = 0;
        while (h.Gunship != null && ticks++ < 60 * 40) h.Idle(1);
        Assert.Null(h.Gunship);
        Assert.InRange(ticks, 60 * 24, 60 * 34);
    }

    [Fact]
    public void A_shot_meeting_a_shell_head_on_stops_both()
    {
        var g = Game();
        g.PutJeep(512, StreetY, 0);
        g.Shells.Add(new Shell { X = 760, Y = StreetY, Vx = -300, Life = 2, Hostile = true });
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Empty(g.Shells);
        Assert.True(g.Jeep.Alive);
    }

    // ───────────────────────────── lives, waves and the whole game ─────────────────────────────

    [Fact]
    public void A_new_jeep_comes_back_away_from_the_tanks()
    {
        var g = Live();
        g.SetLives(3);
        g.PutJeep(512, StreetY);
        g.PutTank(14, 13).FireIn = 99;
        g.Shells.Add(new Shell { X = 512, Y = StreetY - 30, Vy = 300, Life = 1, Hostile = true });
        g.Idle(10);
        Assert.Equal(Phase.Dying, g.Phase);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Jeep.Alive);
        Assert.True(g.Jeep.Safe > 0);
        Assert.True(g.Tanks.All(t => Field.Distance(t.X, t.Y, g.Jeep.X, g.Jeep.Y) > 250));
    }

    [Fact]
    public void The_last_jeep_ends_the_game()
    {
        var g = Live();
        g.SetLives(1);
        g.PutTank(16, 13);
        g.PutJeep(Field.CellX(16) + 10, StreetY);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_last_tank_clears_the_district_with_a_bonus_and_on_to_the_next()
    {
        var g = Live();
        g.PutJeep(512, StreetY, 0);
        g.PutTank(25, 13, Math.PI).Hp = 1;
        g.NoMoreTanks();
        g.Jeep.Safe = 10;
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Equal(Phase.Clear, g.Phase);
        Assert.Equal(1000, g.Bonus);
        int score = g.Score;
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(score + 1000, g.Score);
        Assert.Equal(2, g.Wave);
        Assert.Equal("THE DOCKS", g.City.Name);
        Assert.Equal(g.TanksPerWave, g.TanksToCome);
    }

    [Fact]
    public void An_extra_jeep_every_10000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.ExtraLifeEvery - 50);
        g.PutJeep(512, StreetY, 0);
        g.PutTank(25, 13, Math.PI);
        g.Run(1, FireButton);
        g.Idle(40);
        Assert.Equal(lives + 1, g.Lives);
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_and_the_demos_tour_the_districts()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        var cities = new List<string>();
        for (int round = 0; round < 2; round++)
        {
            g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
            Assert.Equal(GameState.Attract, g.State);
            cities.Add(g.City.Name);
            for (int i = 0; i < (int)(GameEngine.AttractSeconds * 60) + 2 && g.State == GameState.Attract; i++) g.Idle(1);
            Assert.Equal(GameState.HighScores, g.State);
            g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
            Assert.Equal(GameState.Title, g.State);
        }
        Assert.Equal(new[] { "THE OLD TOWN", "THE DOCKS" }, cities);
    }

    [Fact]
    public void Enter_starts_a_game()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(1, g.Wave);
        Assert.Equal(3, g.Lives);
    }

    [Fact]
    public void A_high_score_asks_for_initials_and_goes_in_the_table()
    {
        var g = Live();
        g.SetLives(1);
        g.SetScore(99999);
        g.PutTank(16, 13);
        g.PutJeep(Field.CellX(16) + 10, StreetY);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        g.Idle(60 * 4);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "TNK") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(("TNK", 99999), (g.HighScores.Entries[0].Initials, g.HighScores.Entries[0].Score));
    }

    [Fact]
    public void The_computer_player_clears_the_first_district()
    {
        var g = Real(1);
        for (int i = 0; i < 60 * 120 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on wave {g.Wave}, {g.Tanks.Count + g.TanksToCome} tanks left");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(3, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Jeep.X:0.00} {g.Jeep.Y:0.00} {g.Tanks.Count} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Run(30, new Controls(Drive: 1));
        double y = g.Jeep.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal(y, g.Jeep.Y);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
