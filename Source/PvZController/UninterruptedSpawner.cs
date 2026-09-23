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
        // Day dry-land only – no Dolphin/Snorkel/Ducky, no Zamboni/Bobsled.
        // Weighted pools per tier.
        return tier switch
        {
            0 => Weighted([0, 0, 0, 2, 2, 1, 1, 5]), // normal, cone, newspaper, pole
            1 => Weighted([0,0,2,2,4,4,6,5,3,18]), // add bucket, football light
            2 => Weighted([2,4,4,6,7,8,12,16,17,18,21,3]),
            3 => Weighted([4,4,6,7,12,15,16,18,21,22,23,24,32]),
            _ => Weighted([4,6,7,12,17,18,21,22,23,24,32,15,16]),
        };
    }

    private int Weighted(int[] options) => options[_rng.Next(options.Length)];
}
