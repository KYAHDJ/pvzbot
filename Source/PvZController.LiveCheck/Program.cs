using PvZController;

if (args.Contains("--air-check"))
{
    var strategy = new AutoPlantStrategy();
    var battle = new GameState(5_000_000, 3, 11, 0, true);
    var seeds = new[]
    {
        new SeedPacketState(0, 26, 0, 0, true),
        new SeedPacketState(1, 27, 0, 0, true),
        new SeedPacketState(2, 39, 0, 0, true)
    };
    var coverage = strategy.Choose(battle, [], [], seeds);
    var balloon = strategy.Choose(battle, [], [new ZombieState(3, 16, 400, 200)], seeds);
    var fallback = strategy.Choose(battle, [], [new ZombieState(3, 16, 400, 200)], seeds.Where(s => s.Type != 27).ToArray());
    Console.WriteLine($"Air coverage: {coverage?.Name} row {coverage?.Row + 1}; balloon response: {balloon?.Name}; fallback: {fallback?.Name} row {fallback?.Row + 1}");
    return coverage?.Type == 26 && balloon?.Type == 27 && fallback is { Type: 26, Row: 3 } ? 0 : 6;
}

if (args.Contains("--tactics-check"))
{
    var strategy = new AutoPlantStrategy();
    var battle = new GameState(5_000_000, 3, 11, 0, true);
    var seeds = new[]
    {
        new SeedPacketState(0, 5, 0, 0, true),
        new SeedPacketState(1, 2, 0, 0, true),
        new SeedPacketState(2, 17, 0, 0, true),
        new SeedPacketState(3, 39, 0, 0, true)
    };
    var giant = new ZombieState(2, 23, 520, 3000);
    var slow = strategy.Choose(battle, [], [giant], seeds);
    var afterSlow = new[] { new PlantState(2, 1, 5) };
    var firstEmergency = strategy.Choose(battle, afterSlow, [giant], seeds);
    if (firstEmergency is not null) strategy.RecordAction(firstEmergency, DateTime.UtcNow);
    var reassess = strategy.Choose(battle, afterSlow, [giant], seeds);
    var otherLane = strategy.Choose(battle, afterSlow, [giant, new ZombieState(4, 23, 500, 3000)], seeds);
    Console.WriteLine($"Heavy: {slow?.Name}; first emergency: {firstEmergency?.Name}; immediate follow-up: {reassess?.Name}; other lane: {otherLane?.Name} row {otherLane?.Row + 1}");
    return slow is { Type: 5, Row: 2 } && firstEmergency is { Type: 2, Row: 2 } &&
           reassess?.Type is not (2 or 17 or 20) && otherLane is { Type: 5, Row: 4 } ? 0 : 7;
}

if (args.Contains("--horde-check"))
{
    var strategy = new AutoPlantStrategy();
    var battle = new GameState(5_000_000, 3, 11, 0, true);
    var cards = new[] { 26, 5, 2, 17, 20, 39 }
        .Select((type, index) => new SeedPacketState(index, type, 0, 0, true)).ToArray();
    var wave = new[]
    {
        new ZombieState(0, 2, 170, 460), new ZombieState(0, 7, 220, 1600),
        new ZombieState(1, 4, 190, 900), new ZombieState(1, 0, 225, 270),
        new ZombieState(2, 6, 310, 900), new ZombieState(2, 2, 430, 460),
        new ZombieState(4, 23, 720, 3000), new ZombieState(3, 0, 120, 0)
    };
    var first = strategy.Choose(battle, [], wave, cards);
    if (first is not null) strategy.RecordAction(first, DateTime.UtcNow);
    var second = strategy.Choose(battle, [], wave, cards);
    Console.WriteLine($"Crowded wave: {first?.Name} R{first?.Row + 1} C{first?.Column + 1}; next: {second?.Name} R{second?.Row + 1}");
    return first is { Type: 2, Row: 0 } && second is not { Row: 0, Type: 2 or 17 or 20 } &&
           second is not { Row: 1, Type: 2 or 17 or 20 } ? 0 : 8;
}

if (args.Contains("--layout-check"))
{
    var strategy = new AutoPlantStrategy();
    var battle = new GameState(5_000_000, 3, 11, 0, true);
    var wall = new SeedPacketState(0, 23, 0, 0, true);
    var plants = Enumerable.Range(0, 5).Where(r => r != 2)
        .Select(r => new PlantState(r, 7, 23)).Append(new PlantState(2, 0, 23)).ToList();
    var front = strategy.Choose(battle, plants, [], [wall]);
    if (front is not null) plants.Add(new PlantState(front.Row, front.Column, front.Type));
    var cleanup = strategy.Choose(battle, plants, [], [wall]);
    Console.WriteLine($"Rear Tall-nut: {front?.Name} R{front?.Row + 1} C{front?.Column + 1}; cleanup: {cleanup?.Name} R{cleanup?.Row + 1} C{cleanup?.Column + 1}");
    return front is { Type: 23, Row: 2, Column: 7, UseShovel: false } &&
           cleanup is { Type: 23, Row: 2, Column: 0, UseShovel: true } ? 0 : 9;
}

if (args.Contains("--arsenal-check"))
{
    var arsenal = new VirtualArsenal();
    var now = DateTime.UtcNow;
    var all = arsenal.Available(0, now);
    arsenal.Record(44, now);
    arsenal.Record(35, now);
    var cooling = arsenal.Available(0, now.AddSeconds(1));
    var readyAgain = arsenal.Available(0, now.AddSeconds(51));
    Console.WriteLine($"Dry-land choices={all.Count}; Winter Melon ready={all.Any(s => s.Type == 44)}, cooling={cooling.Any(s => s.Type == 44)}, ready again={readyAgain.Any(s => s.Type == 44)}");
    return all.Count >= 30 && all.All(s => s.Index == -1) &&
        !all.Any(s => s.Type is 1 or 16 or 19 or 24 or 38 or 41 or 43) &&
        all.Any(s => s.Type is 14 or 15 or 40 or 44 or 46) &&
        !cooling.Any(s => s.Type == 44 || VirtualArsenal.NeedsCoffee(s.Type)) &&
        readyAgain.Any(s => s.Type == 44) ? 0 : 12;
}
if (args.Contains("--cob-check"))
{
    var plants = Enumerable.Range(0, 5).SelectMany(r => new[]
    {
        new PlantState(r,0,26),new PlantState(r,1,5),new PlantState(r,7,23),
        new PlantState(r,7,30),new PlantState(r,8,46)
    }).ToArray();
    var cards = new VirtualArsenal().Available(0, DateTime.UtcNow);
    var cobAction = new AutoPlantStrategy().Choose(new GameState(5_000_000,3,11,0,true), plants, [], cards);
    Console.WriteLine($"Cob plan: {cobAction?.Name} R{cobAction?.Row + 1} C{cobAction?.Column + 1}");
    return cobAction is { Type: 47, Row: 2, Column: 3, SeedIndex: -1 } ? 0 : 13;
}
if (args.Contains("--defense-check"))
{
    var cards = new[] { 2,5,39,40,44,17,20,14,15 }
        .Select((type, index) => new SeedPacketState(index,type,0,0,true)).ToArray();
    var rows = Enumerable.Range(0,5).SelectMany(r => new[]
    {
        new PlantState(r,0,26),new PlantState(r,1,5),new PlantState(r,7,23),
        new PlantState(r,8,46)
    }).ToList();
    foreach (var row in new[] { 0,1,3,4 })
        foreach (var col in new[] { 2,3,4 }) rows.Add(new PlantState(row,col,39));
    rows.Add(new PlantState(2,2,39));
    var moderate = new[] { new ZombieState(2,0,680,270),new ZombieState(2,2,690,420),new ZombieState(2,0,700,270) };
    var fill = new AutoPlantStrategy().Choose(new GameState(5_000_000,3,11,0,true),rows,moderate,cards);
    var calm = new AutoPlantStrategy().Choose(new GameState(5_000_000,3,11,0,true),rows,
        [new ZombieState(0,23,750,3000)],cards);
    var pushed = new[] { new PlantState(2,0,26),new PlantState(2,1,5),new PlantState(2,7,23) };
    var wave = Enumerable.Range(0,4).Select(i => new ZombieState(2,2,520+i*15,420)).ToArray();
    var bomb = new AutoPlantStrategy().Choose(new GameState(5_000_000,3,11,0,true),pushed,wave,cards);
    Console.WriteLine($"Thin row: {fill?.Name} R{fill?.Row + 1}; distant giant: {calm?.Name}; pushed row: {bomb?.Name} R{bomb?.Row + 1}");
    return fill is { Row: 2, UseShovel: false } && fill.Type is not (2 or 14 or 15 or 17 or 20) &&
        calm?.Type is not (2 or 14 or 15 or 17 or 20) &&
        bomb is { Row: 2, Type: 2 or 14 or 15 or 17 or 20 } ? 0 : 14;
}

using var pvz = new PvzProcess();
pvz.Connect();
var state = pvz.ReadState();
Console.WriteLine($"Connected: UI={state.GameUi}, mode={state.GameMode}, scene={state.Scene}, sun={state.Sun}, board={state.HasBoard}");
if (args.Contains("--retry-live"))
{
    if (state.GameUi != 4) return 10;
    var retried = pvz.TryAgainAfterLoss(0);
    var retryState = pvz.ReadState();
    Console.WriteLine($"Retry result: clicked={retried}, UI={retryState.GameUi}, board={retryState.HasBoard}");
    return retryState.GameUi == 4 ? 11 : 0;
}
if (args.Contains("--rounds"))
{
    Console.WriteLine($"UI={state.GameUi}, mode={state.GameMode}, endlessRound={pvz.ReadEndlessRounds()}");
    return 0;
}
if (args.Contains("--status"))
{
    if (state.GameUi == 3)
    {
        Console.WriteLine("Cards: " + string.Join(", ", pvz.ReadSeedPackets().Select(s => $"{s.Type}:ready={s.Ready}")));
        Console.WriteLine("Cactus rows: " + string.Join(", ", pvz.ReadPlants().Where(p => p.Type == 26).Select(p => p.Row + 1).Distinct().Order()));
        Console.WriteLine("Balloon rows: " + string.Join(", ", pvz.ReadZombies().Where(z => z.Type == 16).Select(z => z.Row + 1)));
        Console.WriteLine("Plants: " + string.Join(" | ", pvz.ReadPlants().GroupBy(p => p.Row).OrderBy(g => g.Key)
            .Select(g => $"R{g.Key + 1}: " + string.Join(",", g.OrderBy(p => p.Column).Select(p => $"{p.Type}@{p.Column + 1}")))));
        Console.WriteLine("Zombies: " + string.Join(" | ", pvz.ReadZombies().GroupBy(z => z.Row).OrderBy(g => g.Key)
            .Select(g => $"R{g.Key + 1}: " + string.Join(",", g.OrderBy(z => z.X).Select(z => $"{z.Type}@{z.X:0}/hp{z.Health}")))));
    }
    return 0;
}
if (args.Contains("--seed-header"))
{
    Console.WriteLine(string.Join(" ", pvz.ReadSeedBankHeader()));
    Console.WriteLine(pvz.ReadHudDebug());
    return 0;
}
Console.WriteLine($"Paused before check: {pvz.IsPaused()}");
pvz.EnsureUnpaused();
pvz.SetAutoCollect(true);
Console.WriteLine("Auto-collect patch: enabled/adopted successfully");
pvz.SetBackgroundRunning(true);
Console.WriteLine("Background-running patch: enabled/adopted successfully");
if (!state.HasBoard || state.GameUi is not (2 or 3))
{
    Console.WriteLine("NO_ACTION: Open a level's Choose Your Plants screen or active battle.");
    return 2;
}
if (state.GameUi == 2)
{
    state = pvz.ReadState();
    Console.WriteLine($"Strict endless state: mode={state.GameMode}, scene={state.Scene}");
    Console.WriteLine($"Chooser before: selected={pvz.ReadChooserSelectedTypes().Count}, capacity={pvz.ReadSeedCapacity()}");
    var started = pvz.AutoChooseSeedsAndStart(state.Scene);
    Thread.Sleep(1500);
    Console.WriteLine($"Chooser result: UI={pvz.ReadState().GameUi}, selected={pvz.ReadChooserSelectedTypes().Count}, capacity={pvz.ReadSeedCapacity()}");
    return started && pvz.ReadState().GameUi == 3 ? 0 : 5;
}

var before = pvz.ReadPlants();
var seedsBefore = pvz.ReadSeedPackets();
Console.WriteLine("Seeds: " + string.Join(", ", seedsBefore.Select(s => $"#{s.Index + 1}:T{s.Type} ready={s.Ready} cd={s.CooldownPast}/{s.CooldownTotal}")));
var action = new AutoPlantStrategy().Choose(state, before, pvz.ReadZombies(), seedsBefore);
if (action is null)
{
    Console.WriteLine("NO_ACTION: No legal adaptive action is currently ready.");
    return 0;
}
Console.WriteLine($"Chosen: {action.Name}, seed={action.SeedIndex + 1}, R{action.Row + 1}C{action.Column + 1}, reason={action.Reason}");
pvz.PlantUsingSeedCard(action.SeedIndex, action.Row, action.Column);
Thread.Sleep(400);
var after = pvz.ReadPlants();
var seedAfter = pvz.ReadSeedPackets().FirstOrDefault(s => s.Index == action.SeedIndex);
var appeared = after.Count > before.Count || after.Any(p => p.Row == action.Row && p.Column == action.Column && p.Type == action.Type);
Console.WriteLine($"Before={before.Count}, After={after.Count}, Appeared={appeared}, cooldownAfter={seedAfter?.CooldownPast}/{seedAfter?.CooldownTotal}, readyAfter={seedAfter?.Ready}");
return appeared && seedAfter is { Ready: false } ? 0 : 4;



