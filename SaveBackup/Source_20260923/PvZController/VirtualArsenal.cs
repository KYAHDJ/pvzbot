namespace PvZController;

// PvZ has only ten physical seed packets. The bot uses these virtual packets
// for dry-land plants and calls the game's own AddPlant routine to place them.
// Cooldowns remain per plant type even though the bank is hidden.
internal sealed class VirtualArsenal
{
    private readonly Dictionary<int, DateTime> _nextReady = [];
    private static readonly int[] DayLandTypes =
    [
        0,2,3,4,5,6,7,8,10,12,13,14,15,17,18,20,21,22,23,26,27,28,29,30,
        31,32,34,35,36,37,39,40,42,44,46,47
    ];
    private static readonly HashSet<int> Slow = [3,4,12,17,23,30];
    private static readonly HashSet<int> VerySlow = [2,14,15,20,40,42,44,46,47];
    private static readonly HashSet<int> DayMushrooms = [8,10,12,13,14,15,31,42];

    internal static bool NeedsCoffee(int type) => DayMushrooms.Contains(type);

    internal IReadOnlyList<SeedPacketState> Available(int scene, DateTime now)
    {
        if (scene != 0) return [];
        var coffeeReady = !_nextReady.TryGetValue(35, out var coffeeAt) || now >= coffeeAt;
        return DayLandTypes.Where(type => (!DayMushrooms.Contains(type) || coffeeReady) &&
            (!_nextReady.TryGetValue(type, out var ready) || now >= ready))
            .Select(type => new SeedPacketState(-1, type, 0, 0, true)).ToArray();
    }

    internal void Record(int type, DateTime now)
    {
        var duration = VerySlow.Contains(type) ? 50 : Slow.Contains(type) ? 30 : 7.5;
        _nextReady[type] = now.AddSeconds(duration);
    }

    internal void Reset() => _nextReady.Clear();
}
