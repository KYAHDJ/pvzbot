namespace PvZController;

internal sealed class AutoPlantStrategy
{
    private readonly Dictionary<int, DateTime> _lastExplosiveByRow = [];
    private readonly Dictionary<int, (DateTime At, int Column)> _recentCherrySplash = [];
    private static readonly TimeSpan ExplosiveRecheckDelay = TimeSpan.FromSeconds(7);
    private static readonly Dictionary<int, int> Costs = new()
    {
        [0]=100,[1]=50,[2]=150,[3]=50,[4]=25,[5]=175,[6]=150,[7]=200,[8]=0,[9]=25,
        [10]=75,[11]=75,[12]=75,[13]=25,[14]=75,[15]=125,[16]=25,[17]=50,[18]=325,
        [19]=25,[20]=125,[21]=100,[22]=175,[23]=125,[24]=0,[25]=25,[26]=125,[27]=100,
        [28]=125,[29]=125,[30]=125,[31]=100,[32]=100,[33]=25,[34]=100,[35]=75,[36]=50,
        [37]=100,[38]=50,[39]=300,[40]=250,[41]=150,[42]=150,[43]=225,
        [44]=200,[45]=50,[46]=125,[47]=500
    };

    private static readonly int[] SustainedAttackers = [44,40,39,18,7,34,32,29,28,6,0];

    internal PlantAction? Choose(GameState state, IReadOnlyList<PlantState> plants,
        IReadOnlyList<ZombieState> zombies, IReadOnlyList<SeedPacketState> seeds)
    {
        if (!state.HasBoard || state.GameUi != 3) return null;
        zombies = zombies.Where(z => z.Health > 0).ToArray();
        var usable = seeds.Where(s => s.Ready && Cost(s.Type) <= state.Sun).ToList();
        var rows = state.Scene is 2 or 3 ? 6 : 5;
        var threats = Enumerable.Range(0, rows).Select(row => new LaneThreat(row,
            zombies.Where(z => z.Row == row).Sum(z => (Math.Max(0, 850 - z.X) / 100f + 1f) * Math.Max(1, z.Health / 180f)),
            zombies.Where(z => z.Row == row).Select(z => z.X).DefaultIfEmpty(999).Min()))
            .OrderByDescending(x => x.Score).ToList();

        // Protect the house before spending a tick on routine coverage.
        if (ChooseEmergency(state.Scene, plants, zombies, usable, threats, criticalOnly: true) is { } immediate)
            return immediate;

        // Balloon Zombies fly past ordinary projectiles and ground defenses.
        // Blover clears an active balloon immediately; Cactus provides lasting
        // protection in each row even before the first balloon appears.
        var balloonRows = zombies.Where(z => z.Type == 16 && z.Row >= 0 && z.Row < rows)
            .OrderBy(z => z.X).ToList();
        if (balloonRows.Count > 0 && FindSeed(usable, 27) is { } blover)
            for (var row = 0; row < rows; row++)
                if (FindOpenFrom(plants, row, [0,1,2,3,4,5,6,7,8]) is int col)
                    return WithBase(state.Scene, plants, usable, blover, row, col, "clear Balloon Zombies");


        var cactus = FindSeed(usable, 26);
        if (cactus is not null)
            foreach (var row in Enumerable.Range(0, rows)
                         .Where(r => !plants.Any(p => p.Row == r && p.Type == 26))
                         .OrderByDescending(r => balloonRows.Any(z => z.Row == r))
                         .ThenByDescending(r => threats.First(t => t.Row == r).Score))
                if (FindOpenFrom(plants, row, [0,1,2,3,4,5,6]) is int col)
                    return WithBase(state.Scene, plants, usable, cactus, row, col, "protect row from flying zombies");

        // A crowded lane still needs a Cactus. Replace a surplus ordinary
        // attacker when the Cactus card is ready and every tile is occupied.
        if (cactus is not null)
            foreach (var row in Enumerable.Range(0, rows)
                         .Where(r => !plants.Any(p => p.Row == r && p.Type == 26))
                         .OrderByDescending(r => balloonRows.Any(z => z.Row == r)))
            {
                if (FindOpenFrom(plants, row, Enumerable.Range(0, 9)) is not null) continue;
                var extra = plants.Where(p => p.Row == row && IsAttacker(p.Type) && p.Type != 26)
                    .OrderByDescending(p => p.Column).FirstOrDefault();
                if (extra is not null)
                    return PlantAction.Shovel(extra.Row, extra.Column, extra.Type, "make room for anti-air coverage");
            }

        // Slow the highest-health lanes before spending a one-use plant. A
        // Snow Pea must sit behind the target so its shots can reach it.
        if (FindSeed(usable, 5) is { } snowPea)
            foreach (var row in Enumerable.Range(0, rows)
                         .Where(r => zombies.Any(z => z.Row == r && IsHeavyZombie(z)))
                         .Where(r => !plants.Any(p => p.Row == r && p.Type == 5))
                         .OrderByDescending(r => threats.First(t => t.Row == r).Score))
            {
                var heavyX = zombies.Where(z => z.Row == row && IsHeavyZombie(z)).Min(z => z.X);
                if (heavyX < 260) continue; // too close for a new shooter to slow it in time
                var behind = new[] { 1,2,0,3,4,5,6,7,8 }
                    .Where(c => 80 + c * 80 < heavyX - 20);
                if (FindOpenFrom(plants, row, behind) is int col)
                    return WithBase(state.Scene, plants, usable, snowPea, row, col, "slow large zombies");
            }

        if (ChooseEmergency(state.Scene, plants, zombies, usable, threats, criticalOnly: false) is { } response)
            return response;

        // Permanent barriers belong in front of the shooters. If a zombie has
        // already passed that line, an instant plant handles it instead.
        foreach (var row in Enumerable.Range(0, rows).OrderBy(r => threats.First(t => t.Row == r).ClosestX))
            if (!plants.Any(p => p.Row == row && p.Type is 3 or 23 && p.Column >= 6))
                foreach (var type in new[] { 23,3 })
                    if (FindSeed(usable, type) is { } wall &&
                        FindOpenFrom(plants, row, new[] { 7,6 }
                            .Where(c => 80 + c * 80 < threats.First(t => t.Row == row).ClosestX - 30)) is int col)
                        return WithBase(state.Scene, plants, usable, wall, row, col, "build front barrier");

        if (state.Scene == 0 && FindSeed(usable, 47) is { } cob && !plants.Any(p => p.Type == 47))
            foreach (var row in new[] { 2,1,3,0,4 })
                if (threats.First(t => t.Row == row).ClosestX > 650 &&
                    !Occupied(plants, row, 3) && !Occupied(plants, row, 4))
                    return PlantAction.Plant(cob, row, 3, "add long-range Cob Cannon");

        // Build the weakest row before adding optional front-edge upgrades.
        // Crowded rows get extra weight, while low-density rows still catch up.
        foreach (var row in Enumerable.Range(0, rows)
                     .OrderBy(r => plants.Count(p => p.Row == r && IsAttacker(p.Type)) -
                                   Math.Min(2, zombies.Count(z => z.Row == r) / 3))
                     .ThenByDescending(r => threats.First(t => t.Row == r).Score))
        {
            if (plants.Count(p => p.Row == row && IsAttacker(p.Type)) >= 7) continue;
            foreach (var type in SustainedAttackers)
            {
                if (plants.Count(p => p.Row == row && p.Type == type) >= (type == 39 ? 3 : type == 7 ? 2 : 1)) continue;
                if (type == 18 && row is 0 or 4 && plants.Count(p => p.Row == row && IsAttacker(p.Type)) >= 4) continue;
                if (FindSeed(usable, type) is { } seed && FindOpenFrom(plants, row, new[] { 2,3,4,5,6 }
                        .Where(c => 80 + c * 80 < threats.First(t => t.Row == row).ClosestX - 20)) is int col)
                    return WithBase(state.Scene, plants, usable, seed, row, col, "reinforce underfilled row");
            }
        }

        // Ground spikes occupy the forward edge, beyond the permanent wall.
        // Use them only where the scene has ordinary dry ground.
        if (state.Scene == 0)
            foreach (var row in Enumerable.Range(0, rows)
                         .OrderByDescending(r => threats.First(t => t.Row == r).Score))
                if (FindOpenFrom(plants, row, [8]) is int col &&
                    new[] { 46,21 }.Select(type => FindSeed(usable, type)).FirstOrDefault(seed => seed is not null) is { } spike)
                    return PlantAction.Plant(spike, row, col, "reinforce forward ground");

        if (FindSeed(usable, 30) is { } pumpkin)
            foreach (var wall in plants.Where(p => p.Type is 3 or 23 && p.Column >= 6)
                         .OrderBy(p => threats.First(t => t.Row == p.Row).ClosestX))
                if (!plants.Any(p => p.Row == wall.Row && p.Column == wall.Column && p.Type == 30))
                    return PlantAction.Plant(pumpkin, wall.Row, wall.Column, "protect front barrier");

        // Retire the bad rear walls only after a replacement front wall exists
        // and the lane is clear enough to make shoveling safe.
        foreach (var row in Enumerable.Range(0, rows)
                     .Where(r => threats.First(t => t.Row == r).ClosestX > 700))
        {
            var backWall = plants.FirstOrDefault(p => p.Row == row && p.Type is 3 or 23 && p.Column < 6);
            if (backWall is not null && plants.Any(p => p.Row == row && p.Type is 3 or 23 && p.Column >= 6))
                return PlantAction.Shovel(row, backWall.Column, backWall.Type, "clear misplaced rear wall");
        }

        // One Snow Pea per row supplies a consistent slow line without
        // filling the entire lawn with duplicate support plants.
        if (FindSeed(usable, 5) is { } supportSnow)
            foreach (var row in Enumerable.Range(0, rows)
                         .Where(r => !plants.Any(p => p.Row == r && p.Type == 5))
                         .OrderByDescending(r => threats.First(t => t.Row == r).Score))
                if (FindOpenFrom(plants, row, new[] { 1,0,2,3 }
                        .Where(c => 80 + c * 80 < threats.First(t => t.Row == row).ClosestX - 20)) is int col)
                    return WithBase(state.Scene, plants, usable, supportSnow, row, col, "complete slow coverage");

        // Move an old midfield Cactus back by planting its replacement first.
        if (cactus is not null)
            foreach (var row in Enumerable.Range(0, rows)
                         .Where(r => threats.First(t => t.Row == r).ClosestX > 700))
                if (plants.Any(p => p.Row == row && p.Type == 26 && p.Column > 2) &&
                    !plants.Any(p => p.Row == row && p.Type == 26 && p.Column <= 2) &&
                    FindOpenFrom(plants, row, [0,1,2]) is int col)
                    return WithBase(state.Scene, plants, usable, cactus, row, col, "move anti-air behind attackers");
        foreach (var oldCactus in plants.Where(p => p.Type == 26 && p.Column > 2))
            if (threats.First(t => t.Row == oldCactus.Row).ClosestX > 700 &&
                plants.Any(p => p.Row == oldCactus.Row && p.Type == 26 && p.Column <= 2))
                return PlantAction.Shovel(oldCactus.Row, oldCactus.Column, oldCactus.Type, "clear duplicate midfield Cactus");

        // Upgrade a settled row without leaving it exposed during a wave.
        if (state.Scene == 0)
            foreach (var (baseType, upgradeType) in new[] { (39,44), (7,40) })
                if (FindSeed(usable, upgradeType) is not null)
                    foreach (var row in Enumerable.Range(0, rows)
                                 .Where(r => threats.First(t => t.Row == r).ClosestX > 700))
                    {
                        if (plants.Any(p => p.Row == row && p.Type == upgradeType)) continue;
                        if (FindOpenFrom(plants, row, [2,3,4,5,6]) is not null) continue;
                        var surplus = plants.Where(p => p.Row == row && p.Type == baseType)
                            .OrderByDescending(p => p.Column).FirstOrDefault();
                        if (surplus is not null)
                            return PlantAction.Shovel(row, surplus.Column, surplus.Type, $"make room for stronger {PlantAction.PlantName(upgradeType)}");
                    }

        if (FindSeed(usable, 22) is { } torchwood)
            foreach (var row in Enumerable.Range(0, rows).OrderByDescending(r => threats.First(t => t.Row == r).Score))
                if (!plants.Any(p => p.Row == row && p.Type == 22) &&
                    plants.Any(p => p.Row == row && p.Column < 6 && IsPeaFamily(p.Type)) && !Occupied(plants, row, 6))
                    return WithBase(state.Scene, plants, usable, torchwood, row, 6, "boost pea row");

        return null;
    }

    private PlantAction? ChooseEmergency(int scene, IReadOnlyList<PlantState> plants,
        IReadOnlyList<ZombieState> zombies, IReadOnlyList<SeedPacketState> usable,
        IReadOnlyList<LaneThreat> threats, bool criticalOnly)
    {
        var crowdedNearHouse = zombies.Count(z => z.X < 550) >= 6;
        foreach (var danger in threats.Where(t =>
                     criticalOnly
                         ? t.ClosestX < 280 || crowdedNearHouse && t.ClosestX < 450
                         : ShouldSpendInstantPlant(t, plants, zombies))
                 .OrderBy(t => t.ClosestX).ThenByDescending(t => t.Score))
        {
            var delay = danger.ClosestX < 180 ? TimeSpan.FromSeconds(2.5) : ExplosiveRecheckDelay;
            if (_lastExplosiveByRow.TryGetValue(danger.Row, out var last) &&
                DateTime.UtcNow - last < delay) continue;
            var targetColumn = Math.Clamp((int)Math.Round((danger.ClosestX - 80) / 80), 0, 8);
            if (_recentCherrySplash.TryGetValue(danger.Row, out var splash) &&
                DateTime.UtcNow - splash.At < TimeSpan.FromSeconds(5) &&
                Math.Abs(targetColumn - splash.Column) <= 1) continue;
            var laneZombies = zombies.Where(z => z.Row == danger.Row).ToList();
            var spread = laneZombies.Max(z => z.X) - laneZombies.Min(z => z.X);
            var adjacentCluster = zombies.Count(z => Math.Abs(z.Row - danger.Row) <= 1 &&
                Math.Abs(z.X - danger.ClosestX) < 130);
            var priority = laneZombies.Count >= 3 && spread > 160 && adjacentCluster < 3
                ? new[] { 20, 2, 17, 15, 14 } : new[] { 2, 20, 17, 15, 14 };
            foreach (var type in priority)
                if (FindSeed(usable, type) is { } emergency &&
                    FindEmergencySpot(plants, danger.Row, targetColumn, type) is { } spot)
                    return WithBase(scene, plants, usable, emergency, spot.Row, spot.Column,
                        criticalOnly ? "stop breach near house" : "stop advancing zombies");
        }
        return null;
    }

    private static bool ShouldSpendInstantPlant(LaneThreat lane, IReadOnlyList<PlantState> plants,
        IReadOnlyList<ZombieState> zombies)
    {
        if (lane.ClosestX >= 700) return false;
        var count = zombies.Count(z => z.Row == lane.Row);
        if (count == 0) return false;
        var combatColumns = plants.Where(p => p.Row == lane.Row &&
            (IsAttacker(p.Type) || p.Type is 3 or 23 or 47))
            .Select(p => p.Column).Distinct().Count();
        var collapsing = combatColumns <= 4 && lane.ClosestX < 650 && lane.Score >= 8;
        var breach = lane.ClosestX < 450 && (count >= 2 || lane.Score >= 12);
        var horde = lane.ClosestX < 620 && (count >= 5 ||
            count >= 3 && lane.Score >= 30);
        return collapsing || breach || horde;
    }

    internal void RecordAction(PlantAction action, DateTime when)
    {
        if (!action.UseShovel && action.Type is 2 or 14 or 15 or 17 or 20)
            _lastExplosiveByRow[action.Row] = when;
        if (!action.UseShovel && action.Type == 2)
            foreach (var row in new[] { action.Row - 1, action.Row + 1 }.Where(r => r is >= 0 and < 6))
                _recentCherrySplash[row] = (when, action.Column);
    }

    private static PlantAction? WithBase(int scene, IReadOnlyList<PlantState> plants, IReadOnlyList<SeedPacketState> usable,
        SeedPacketState wanted, int row, int col, string reason)
    {
        if (scene is 2 or 3 && row is 2 or 3 && !plants.Any(p => p.Row == row && p.Column == col && p.Type == 16))
            return FindSeed(usable, 16) is { } lily ? PlantAction.Plant(lily, row, col, "prepare water tile") : null;
        if (scene == 4 && !plants.Any(p => p.Row == row && p.Column == col && p.Type == 33))
            return FindSeed(usable, 33) is { } pot ? PlantAction.Plant(pot, row, col, "prepare roof tile") : null;
        return PlantAction.Plant(wanted, row, col, reason);
    }

    private static SeedPacketState? FindSeed(IEnumerable<SeedPacketState> seeds, int type) => seeds.FirstOrDefault(s => s.Type == type);
    private static int Cost(int type) => Costs.GetValueOrDefault(type, 9999);
    private static bool IsPeaFamily(int type) => type is 0 or 7 or 18 or 28 or 40;
    private static bool IsHeavyZombie(ZombieState zombie) => zombie.Type is 7 or 12 or 22 or 23 or 32 || zombie.Health >= 900;
    private static bool IsAttacker(int type) => type is 0 or 5 or 6 or 7 or 8 or 10 or 13 or 18 or 24 or 26 or 28 or 29 or 32 or 34 or 39 or 40 or 44;
    private static bool IsBarrier(int type) => type is 3 or 23 or 30;
    private static bool IsTemporaryOrBase(int type) => type is 2 or 4 or 14 or 15 or 16 or 17 or 19 or 20 or 21 or 33 or 35;
    private static bool Occupied(IEnumerable<PlantState> plants, int row, int col) => plants.Any(p => p.Row == row &&
        (p.Column == col || p.Type == 47 && p.Column + 1 == col) && p.Type is not (16 or 30 or 33 or 35));
    private static int? FindOpenFrom(IReadOnlyList<PlantState> plants, int row, IEnumerable<int> columns)
    { foreach (var c in columns) if (!Occupied(plants, row, c)) return c; return null; }
    private static (int Row, int Column)? FindEmergencySpot(IReadOnlyList<PlantState> plants, int row, int column, int type)
    {
        foreach (var c in Enumerable.Range(0, 9).OrderBy(c => Math.Abs(c - column)))
            if ((type == 20 || Math.Abs(c - column) <= 1) && !Occupied(plants, row, c))
                return (row, c);
        return null;
    }
    private sealed record LaneThreat(int Row, float Score, float ClosestX);
}

internal sealed record PlantAction(int SeedIndex, int Row, int Column, int Type, string Name, string Reason, bool UseShovel)
{
    internal static PlantAction Plant(SeedPacketState seed, int row, int col, string reason) =>
        new(seed.Index,row,col,seed.Type,PlantName(seed.Type),reason,false);
    internal static PlantAction Shovel(int row, int col, int type, string reason) =>
        new(-1,row,col,type,PlantName(type),reason,true);
    internal static string PlantName(int type) => type switch
    {
        0=>"Peashooter",1=>"Sunflower",2=>"Cherry Bomb",3=>"Wall-nut",4=>"Potato Mine",5=>"Snow Pea",7=>"Repeater",
        8=>"Puff-shroom",9=>"Sun-shroom",10=>"Fume-shroom",13=>"Scaredy-shroom",14=>"Ice-shroom",15=>"Doom-shroom",
        16=>"Lily Pad",17=>"Squash",18=>"Threepeater",19=>"Tangle Kelp",20=>"Jalapeno",21=>"Spikeweed",22=>"Torchwood",23=>"Tall-nut",
        24=>"Sea-shroom",25=>"Plantern",26=>"Cactus",27=>"Blover",28=>"Split Pea",29=>"Starfruit",30=>"Pumpkin",31=>"Magnet-shroom",
        32=>"Cabbage-pult",33=>"Flower Pot",34=>"Kernel-pult",35=>"Coffee Bean",36=>"Garlic",37=>"Umbrella Leaf",38=>"Marigold",39=>"Melon-pult",
        40=>"Gatling Pea",41=>"Twin Sunflower",42=>"Gloom-shroom",43=>"Cattail",44=>"Winter Melon",45=>"Gold Magnet",46=>"Spikerock",47=>"Cob Cannon",_=>$"Plant {type}"
    };
}
