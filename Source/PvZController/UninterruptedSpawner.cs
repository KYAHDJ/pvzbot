namespace PvZController;

/// <summary>Controls continuous Day-lawn spawns for the uninterrupted stream mode.
/// Intended to be called from the bot tick while in battle (GameUi 3).
/// Keeps live zombie count bounded and difficulty rising slowly.</summary>
internal sealed class UninterruptedSpawner
{
    private DateTime _start = DateTime.MinValue;
    private DateTime _nextSpawn = DateTime.MinValue;
    private int _totalSpawned;
    private readonly Random _rng = new();

    internal void Reset(DateTime now)
    {
        _start = now;
        _nextSpawn = now.AddSeconds(2);
        _totalSpawned = 0;
    }

    internal bool ShouldReset => _start == DateTime.MinValue;

    internal void Tick(PvzProcess pvz, GameState state, IReadOnlyList<ZombieState> zombies, DateTime now)
    {
        if (_start == DateTime.MinValue) Reset(now);
        if (state.GameUi != 3 || !state.HasBoard) return;
        if (zombies.Count >= 32) return; // cap live zombies for stability
        if (now < _nextSpawn) return;

        // Difficulty curve: time since start influences interval and zombie tier.
        var elapsed = (now - _start).TotalSeconds;
        double baseInterval;
        if (elapsed < 120) baseInterval = 2600;
        else if (elapsed < 300) baseInterval = 2100;
        else if (elapsed < 600) baseInterval = 1700;
        else if (elapsed < 1200) baseInterval = 1350;
        else baseInterval = 1100;
        // Slight jitter to avoid mechanical pattern.
        baseInterval += _rng.Next(-140, 140);
        // Crowding slows spawns a bit.
        if (zombies.Count > 18) baseInterval += 500;
        if (zombies.Count > 24) baseInterval += 700;

        _nextSpawn = now.AddMilliseconds(Math.Clamp(baseInterval, 800, 4000));

        // Choose tier based on elapsed and random.
        var tier = elapsed < 60 ? 0 : elapsed < 180 ? 1 : elapsed < 400 ? 2 : elapsed < 750 ? 3 : 4;
        int type = PickType(tier);
        var rows = state.Scene is 2 or 3 ? 6 : 5;
        var row = _rng.Next(rows);
        try { pvz.SpawnZombie(row, 8, type); _totalSpawned++; } catch { }
    }

    private int PickType(int tier)
    {
        // EASY ONLY - per user request: no giant/bungee/hard zombies.
        // Only basic Day zombies so chat via StreamToEarn makes it hard, not the game.
        // Types: 0 Zombie, 1 Flag, 2 Conehead, 3 Pole Vault, 4 Bucket, 5 Newspaper, 6 Screen Door
        // Football (7) is borderline, keep rare. No Dancing, no Balloon, no Digger, no Catapult, no Gargantuar etc.
        return tier switch
        {
            0 => Weighted([0, 0, 0, 2, 2, 1]), // mostly normal + cone
            1 => Weighted([0, 2, 2, 4, 5, 3]), // add bucket, newspaper, pole
            2 => Weighted([0, 2, 4, 5, 6, 3, 1]), // add screen door, flag
            3 => Weighted([2, 4, 6, 5, 3, 0, 1, 7]), // football rare
            _ => Weighted([2, 4, 6, 5, 0, 3, 1]), // keep easy forever
        };
    }

    private int Weighted(int[] options) => options[_rng.Next(options.Length)];
}
