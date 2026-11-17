using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text.Json;
using System.Collections.Concurrent;

namespace PvZController;

public partial class MainWindow : Window
{
    private readonly PvzProcess _pvz = new();
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _botTimer;
    private readonly DispatcherTimer _yapTimer;
    private readonly AutoPlantStrategy _strategy = new();
    private readonly VirtualArsenal _virtualArsenal = new();
    private readonly StreamToEarnBridge _streamBridge = new();
    private readonly VoicePersona _voice = new();
    private readonly PersonaLines _persona = new();
    private readonly SubtitleWindow _subtitle = new();
    private readonly StreamStats _stats = new();
    private readonly ChatBridgeLauncher _chat = new();
    private DateTime _nextChatVoice = DateTime.MinValue;
    private DateTime _nextJoinVoice = DateTime.MinValue;
    private readonly UninterruptedSpawner _spawner = new();
    private DateTime _nextBattleCommentary = DateTime.MinValue;
    private DateTime _lastChatTime = DateTime.MinValue;
    private string _lastChatEffect = "";
    private readonly ConcurrentQueue<StreamEffect> _streamEffects = new();
    private readonly DispatcherTimer _streamTimer;
    private int _queuedStreamEffects;
    private int _droppedStreamEffects;
    private bool _botRunning;
    private bool _botBusy;
    private bool _seedChooserHandled;
    private DateTime _nextBotAction = DateTime.MinValue;
    private DateTime _nextRewardCollect = DateTime.MinValue;
    private DateTime _nextCobFire = DateTime.MinValue;
    private DateTime _lossSeenAt = DateTime.MinValue;
    private DateTime _nextRetryAttempt = DateTime.MinValue;
    private int _retryAttempts;
    private int _lastBattleRound = -1;
    private DateTime _nextFiller = DateTime.UtcNow.AddSeconds(5);
    private DateTime _nextEvent = DateTime.MinValue;
    private DateTime _nextBreach = DateTime.MinValue;
    private DateTime _nextHorde = DateTime.MinValue;
    private DateTime _lastShovel = DateTime.MinValue;
    private HashSet<(int, int, int)> _prevPlants = [];
    private Dictionary<int, int> _prevZombieTypes = [];
    private bool _eventsPrimed;
    private int _lastBlockedCount = -1;
    private int _menuStage;
    private int _lossStep;
    private DateTime _speakingUntil = DateTime.MinValue;
    private DateTime _nextStatsPaint = DateTime.MinValue;
    private DateTime _nextViewerVoice = DateTime.MinValue;
    private DateTime _menuNext = DateTime.MinValue;
    private DateTime _noBoardSince = DateTime.MinValue;
    private readonly Queue<string> _fillerBag = new();

    public MainWindow()
    {
        InitializeComponent();
        _voice.Speaking += line => Dispatcher.BeginInvoke(new Action(() =>
        {
            // Prefer the game's own message box (part of the game picture, so Game Capture shows it).
            if (_lossSeenAt != DateTime.MinValue) return; // loss screen is scrolled sideways; short captions are used instead
            _speakingUntil = DateTime.UtcNow.AddSeconds(15);
            try { _pvz.ShowGameMessage(line, 7); }
            catch { _subtitle.ShowLine(line, _pvz.GameWindow); }
        }));
        _voice.Finished += () =>
        {
            _nextFiller = DateTime.UtcNow.AddSeconds(6 + Random.Shared.NextDouble() * 2);
            _speakingUntil = DateTime.UtcNow.AddSeconds(1);
        };
        PlantChoice.ItemsSource = Plants;
        ZombieChoice.ItemsSource = Zombies;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshTimer.Tick += (_, _) => { TryAutoAttachAndRun(); RefreshState(silent: true); };
        _refreshTimer.Start();
        _botTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _botTimer.Tick += (_, _) => RunBotTick();
        _yapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _yapTimer.Tick += (_, _) => ContinuousYap();
        _yapTimer.Start();
        _streamTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _streamTimer.Tick += (_, _) =>
        {
            if (_streamEffects.TryDequeue(out var effect))
            {
                Interlocked.Decrement(ref _queuedStreamEffects);
                HandleStreamEffect(effect);
            }
            var dropped = Interlocked.Exchange(ref _droppedStreamEffects, 0);
            if (dropped > 0) Log($"STREAM skipped {dropped} events during an overload burst.");
        };
        _streamBridge.EffectReceived += effect =>
        {
            if (Interlocked.Increment(ref _queuedStreamEffects) > 256)
            {
                Interlocked.Decrement(ref _queuedStreamEffects);
                Interlocked.Increment(ref _droppedStreamEffects);
                return;
            }
            _streamEffects.Enqueue(effect);
        };
        _streamTimer.Start();
        try { _streamBridge.Start(); Log("StreamToEarn bridge ready on 127.0.0.1:8082."); }
        catch (Exception ex) { Log($"StreamToEarn bridge unavailable: {ex.Message}"); }
        _chat.Message += text => Dispatcher.BeginInvoke(new Action(() => Log(text)));
        _botRunning = true;
        _botTimer.Start();
        BotStateText.Text = "Bot: always on — waiting for PvZ";
        Log("Always-on combat bot ready. Start PvZ; connection and play begin automatically.");
        Loaded += (_, _) => Dispatcher.BeginInvoke(TryAutoAttachAndRun);
    }

    private static IReadOnlyList<GameItem> Plants =>
    [
        new(0,"Peashooter"),new(1,"Sunflower"),new(2,"Cherry Bomb"),new(3,"Wall-nut"),
        new(4,"Potato Mine"),new(5,"Snow Pea"),new(6,"Chomper"),new(7,"Repeater"),
        new(8,"Puff-shroom"),new(9,"Sun-shroom"),new(10,"Fume-shroom"),new(11,"Grave Buster"),
        new(12,"Hypno-shroom"),new(13,"Scaredy-shroom"),new(14,"Ice-shroom"),new(15,"Doom-shroom"),
        new(16,"Lily Pad"),new(17,"Squash"),new(18,"Threepeater"),new(19,"Tangle Kelp"),
        new(20,"Jalapeno"),new(21,"Spikeweed"),new(22,"Torchwood"),new(23,"Tall-nut"),
        new(24,"Sea-shroom"),new(25,"Plantern"),new(26,"Cactus"),new(27,"Blover"),
        new(28,"Split Pea"),new(29,"Starfruit"),new(30,"Pumpkin"),new(31,"Magnet-shroom"),
        new(32,"Cabbage-pult"),new(33,"Flower Pot"),new(34,"Kernel-pult"),new(35,"Coffee Bean"),
        new(36,"Garlic"),new(37,"Umbrella Leaf"),new(38,"Marigold"),new(39,"Melon-pult"),
        new(40,"Gatling Pea"),new(41,"Twin Sunflower"),new(42,"Gloom-shroom"),new(43,"Cattail"),
        new(44,"Winter Melon"),new(45,"Gold Magnet"),new(46,"Spikerock"),new(47,"Cob Cannon")
    ];

    private static IReadOnlyList<GameItem> Zombies =>
    [
        new(0, "Zombie"), new(1, "Flag Zombie"), new(2, "Conehead Zombie"),
        new(3, "Pole Vaulting Zombie"), new(4, "Buckethead Zombie"),
        new(5, "Newspaper Zombie"), new(6, "Screen Door Zombie"),
        new(7, "Football Zombie"), new(8, "Dancing Zombie"),
        new(11, "Ducky Tube Zombie"), new(12, "Snorkel Zombie"),
        new(15, "Dolphin Rider Zombie"), new(16, "Jack-in-the-Box Zombie"),
        new(17, "Balloon Zombie"), new(18, "Digger Zombie"),
        new(19, "Pogo Zombie"), new(21, "Bungee Zombie"),
        new(22, "Ladder Zombie"), new(23, "Catapult Zombie"),
        new(24, "Gargantuar"), new(32, "Giga-gargantuar")
    ];

    private void Connect_Click(object sender, RoutedEventArgs e) => Run("Connect", () =>
    {
        _pvz.Connect();
        _pvz.SetAutoCollect(true);
        _pvz.SetBackgroundRunning(true);
        AutoCollectCheck.IsChecked = true;
        StatusText.Text = "Connected — verified 1.2.0.1073";
        StatusText.Foreground = Brushes.LightGreen;
        Log($"Connected to verified game: {_pvz.ExecutablePath}");
        RefreshState();
    });

    private void Disconnect_Click(object sender, RoutedEventArgs e) => Run("Disconnect", () =>
    {
        StopBot();
        _pvz.Disconnect();
        SetDisconnected("Disconnected");
        Log("Disconnected; controller-owned changes restored where the game was available.");
    });

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshState();

    private void SetSun_Click(object sender, RoutedEventArgs e) => Run("Set sun", () =>
    {
        if (!int.TryParse(SunInput.Text, out var value)) throw new InvalidOperationException("Enter a whole number for sun.");
        _pvz.SetSun(value);
        Log($"Sun set to {value}.");
        RefreshState();
    });

    private void AutoCollect_Click(object sender, RoutedEventArgs e) => Run("Auto-collect", () =>
    {
        var enabled = AutoCollectCheck.IsChecked == true;
        _pvz.SetAutoCollect(enabled);
        Log($"Auto-collect {(enabled ? "enabled" : "disabled")}.");
    }, () => AutoCollectCheck.IsChecked = !AutoCollectCheck.IsChecked);

    private void FreePlanting_Click(object sender, RoutedEventArgs e) => Run("Free planting", () =>
    {
        var enabled = FreePlantingCheck.IsChecked == true;
        _pvz.SetFreePlanting(enabled);
        Log($"Free planting {(enabled ? "enabled" : "disabled")}.");
    }, () => FreePlantingCheck.IsChecked = !FreePlantingCheck.IsChecked);

    private void PlacePlant_Click(object sender, RoutedEventArgs e) => Run("Place plant", () =>
    {
        var plant = (GameItem?)PlantChoice.SelectedItem ?? throw new InvalidOperationException("Select a plant.");
        var (row, column) = SelectedCell();
        _pvz.PlacePlant(row, column, plant.Id);
        Log($"Placed {plant.Name} at row {row + 1}, column {column + 1}.");
    });

    private void SpawnZombie_Click(object sender, RoutedEventArgs e) => Run("Spawn zombie", () =>
    {
        var zombie = (GameItem?)ZombieChoice.SelectedItem ?? throw new InvalidOperationException("Select a zombie.");
        var (row, column) = SelectedCell();
        _pvz.SpawnZombie(row, column, zombie.Id);
        Log($"Spawned {zombie.Name} at row {row + 1}, column {column + 1}.");
    });

    private void BotStart_Click(object sender, RoutedEventArgs e)
    {
        Run("Start bot", () =>
        {
            var state = _pvz.ReadState();
            _pvz.EnsureUnpaused();
            if (!state.HasBoard || state.GameUi is not (2 or 3))
                throw new InvalidOperationException("Enter Choose Your Plants or an active battle before starting the bot.");
            _botRunning = true;
            if (ContinuousEndlessCheck.IsChecked == true)
                _pvz.EnableStrictDayEndless();
            _pvz.SetAutoCollect(true);
            _pvz.SetBackgroundRunning(true);
            AutoCollectCheck.IsChecked = true;
            _botTimer.Start();
            BotStateText.Text = "Bot: running — reading the board";
            Log("Adaptive bot started. Real seed-card cooldowns and sun costs are active.");
            RunBotTick();
        });
    }

    private void BotPause_Click(object sender, RoutedEventArgs e)
    {
        _botRunning = false;
        _botTimer.Stop();
        BotStateText.Text = "Bot: paused";
        Log("Bot paused.");
    }

    private void BotStop_Click(object sender, RoutedEventArgs e)
    {
        StopBot();
        Log("Bot stopped.");
    }

    private void EmergencyStop_Click(object sender, RoutedEventArgs e)
    {
        StopBot();
        try { _pvz.RestoreOwnedChanges(); } catch (Exception ex) { Log($"Restore warning: {ex.Message}"); }
        _pvz.Disconnect();
        AutoCollectCheck.IsChecked = false;
        FreePlantingCheck.IsChecked = false;
        SetDisconnected("Emergency stop active");
        Log("EMERGENCY STOP: bot stopped, owned patches restored, and game detached.");
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _refreshTimer.Stop();
        _botTimer.Stop();
        _yapTimer.Stop();
        _streamTimer.Stop();
        StopBot();
        _streamBridge.Dispose();
        _chat.Dispose();
        _voice.Dispose();
        _subtitle.Close();
        _pvz.Dispose();
    }

    private void TryAutoAttachAndRun()
    {
        if (_pvz.IsConnected)
        {
            _botRunning = true;
            if (!_botTimer.IsEnabled) _botTimer.Start();
            return;
        }
        try
        {
            _pvz.Connect();
            _pvz.SetAutoCollect(true);
            _pvz.SetBackgroundRunning(true);
            AutoCollectCheck.IsChecked = true;
            _botRunning = true;
            _botTimer.Start();
            StatusText.Text = "Connected automatically — StreamToEarn ready";
            StatusText.Foreground = Brushes.LightGreen;
            BotStateText.Text = "Bot: always on — waiting for a battle";
            Log("Automatically connected to PvZ; combat bot is running.");
            SayCategory("connect");
        }
        catch { }
    }

    private void HandleStreamEffect(StreamEffect effect)
    {
        try
        {
            if (effect.Id.StartsWith("Chat", StringComparison.Ordinal)) { HandleChat(effect); return; }
            TryAutoAttachAndRun();
            if (!_pvz.IsConnected) throw new InvalidOperationException("PvZ is not running.");
            // Track last chat time for separating chat vs bored lines
            _lastChatTime = DateTime.UtcNow;
            _lastChatEffect = effect.Id;
            switch (effect.Id)
            {
                case "EasyStart": _botRunning = true; _botTimer.Start(); break;
                case "SetSun": _pvz.SetSun(JsonInt(effect.Payload, "value", 5_000_000)); break;
                case "FreePlantsOn": _pvz.SetFreePlanting(true); break;
                case "FreePlantsOff": _pvz.SetFreePlanting(false); break;
                case "KillAllZombies": _pvz.KillAllZombies(); break;
                case "ClearAllPlants": _pvz.ClearAllPlants(); break;
                case "PutZombie":
                {
                    var count = 0;
                    try { count = _pvz.ReadZombies().Count; } catch { }
                    if (count >= 45)
                    {
                        Log("STREAM zombie spawn deferred: 45 zombies are already on screen.");
                        // Still react with voice even when deferred
                        break;
                    }
                    var state = _pvz.ReadState();
                    var rows = state.Scene is 2 or 3 ? 6 : 5;
                    _pvz.SpawnZombie(Random.Shared.Next(rows), 8, ResolveItem(effect.Payload, "type", Zombies));
                    break;
                }
                case "PutPlant":
                {
                    // Chat can't plant per user request - log but don't place, just voice
                    Log($"STREAM PutPlant ignored - chat can only spawn zombies / clear plants");
                    break;
                }
                // Gifts / Follows / Likes via StreamToEarn - separate from bored yapping
                case "Gift":
                case "Follow":
                case "Like":
                case "Share":
                case "Donation":
                    // No game action, just voice
                    break;
                default: Log($"STREAM effect not implemented yet: {effect.Id}"); 
                    // Still voice for unknown gift-like effects
                    break;
            }
            Log($"STREAM → {effect.Id}");
            var extra = effect.Id == "PutZombie" && effect.Payload.ValueKind == JsonValueKind.Object &&
                        effect.Payload.TryGetProperty("type", out var zt) && zt.ValueKind == JsonValueKind.String
                ? new Dictionary<string, string> { ["zombie"] = zt.GetString()! }
                : new Dictionary<string, string> { ["zombie"] = "zombie" };
            // Gift bursts must not chop the voice: react at most every 4 seconds.
            if (DateTime.UtcNow >= _nextViewerVoice)
            {
                _nextViewerVoice = DateTime.UtcNow.AddSeconds(4);
                SayCategoryInterrupt(StreamCategory(effect.Id), extra);
            }
        }
        catch (Exception ex) { Log($"STREAM {effect.Id} failed: {ex.Message}"); }
    }

    private Dictionary<string, string> Vars(Dictionary<string, string>? extra = null)
    {
        var d = new Dictionary<string, string>
        {
            ["run"] = Math.Max(1, _stats.Runs).ToString(),
            ["losses"] = _stats.Losses.ToString(),
            ["time"] = StreamStats.Clock(_stats.InRun ? _stats.RunSeconds : _stats.LastRunSeconds),
            ["best"] = StreamStats.Clock(_stats.BestSeconds),
        };
        if (extra is not null) foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    private void SayCategory(string category, bool urgent = false, Dictionary<string, string>? extra = null)
    {
        if (VoiceCheck.IsChecked != true) return;
        var line = _persona.Next(category, Vars(extra));
        if (line is not null && _voice.TrySay(line, urgent)) Log($"VOICE → {line}");
    }

    // Short text only: the loss screen scrolls the view sideways, so long lines get cut off.
    private void LossCaption(string text)
    {
        try { _pvz.ShowGameMessage(text, 7); } catch { }
    }

    private static string JsonStr(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    // Real TikTok chat: say the viewer's name. Rate limited so a busy chat never turns into spam.
    private void HandleChat(StreamEffect effect)
    {
        var name = ChatFilter.CleanName(JsonStr(effect.Payload, "user"), JsonStr(effect.Payload, "handle"));
        var now = DateTime.UtcNow;
        var vars = new Dictionary<string, string> { ["name"] = name };
        switch (effect.Id)
        {
            case "ChatJoin":
                if (now < _nextJoinVoice || now < _nextChatVoice) return;
                _nextJoinVoice = now.AddSeconds(15);
                _nextChatVoice = now.AddSeconds(3);
                SayChat("chatjoin", vars, interrupt: false);
                break;
            case "ChatFollow":
                _nextChatVoice = now.AddSeconds(4);
                SayChat("chatfollow", vars, interrupt: true);
                break;
            case "ChatGift":
            {
                var gift = ChatFilter.CleanText(JsonStr(effect.Payload, "gift"));
                vars["gift"] = gift.Length == 0 || ChatFilter.IsDirty(gift) ? "gift" : gift;
                _nextChatVoice = now.AddSeconds(4);
                SayChat("chatgift", vars, interrupt: true);
                break;
            }
            case "ChatComment":
            {
                if (now < _nextChatVoice) return;
                var text = ChatFilter.CleanText(JsonStr(effect.Payload, "text"));
                if (text.Length == 0) return;
                var lower = text.ToLowerInvariant();
                string category;
                if (ChatFilter.IsDirty(text) || ChatFilter.IsDirty(name)) category = "chatgeneric";
                else if (lower.Split(' ', ',', '!', '.').Any(w => w is "hi" or "hello" or "hey" or "yo" or "sup" or "hii" or "heyy")) category = "chatgreet";
                else if (lower.Contains("trash") || lower.Contains("noob") || lower.Contains("bad") || lower.Contains("suck") || lower.Contains("lose") || lower.Contains("ez"))
                    category = "chattaunt";
                else if (lower.Contains("gg") || lower.Contains("good") || lower.Contains("nice") || lower.Contains("love") || lower.Contains("best") || lower.Contains("cool") || lower.Contains("goat"))
                    category = "chatthanks";
                else if (text.Length <= 40 && Random.Shared.Next(3) > 0) { category = "chatcomment"; vars["text"] = text; }
                else category = "chatgeneric";
                _nextChatVoice = now.AddSeconds(6);
                SayChat(category, vars, interrupt: true);
                break;
            }
        }
        Log($"CHAT {effect.Id[4..]} from {name}");
    }

    private void SayChat(string category, Dictionary<string, string> vars, bool interrupt)
    {
        if (VoiceCheck.IsChecked != true) return;
        var line = _persona.Next(category, Vars(vars));
        if (line is null) return;
        var ok = interrupt ? _voice.InterruptAndSay(line) : _voice.TrySay(line, true);
        if (ok) Log($"VOICE → {line} [CHAT]");
    }

    private void SayForce(string category)
    {
        if (VoiceCheck.IsChecked != true) return;
        var line = _persona.Next(category, Vars());
        if (line is not null && _voice.TrySayForce(line)) Log($"VOICE → {line}");
    }

    private void SayCategoryInterrupt(string category, Dictionary<string, string>? extra = null)
    {
        if (VoiceCheck.IsChecked != true) return;
        var line = _persona.Next(category, Vars(extra));
        if (line is not null && _voice.InterruptAndSay(line)) Log($"VOICE → {line} [NOW]");
    }

    private static string ZombieName(int type) =>
        Zombies.FirstOrDefault(z => z.Id == type)?.Name ?? "zombie";

    private static readonly HashSet<int> NotableZombies = [3, 4, 5, 6, 7, 8, 16, 17, 18, 19, 21, 22, 23, 24, 32];

    // Talk about what is really on screen. Highest priority first; one event at a time.
    private void DetectEvents(IReadOnlyList<ZombieState> zombies, IReadOnlyList<PlantState> plants)
    {
        var now = DateTime.UtcNow;
        var curPlants = plants.Where(p => p.Type != 99).Select(p => (p.Row, p.Column, p.Type)).ToHashSet();
        var curTypes = zombies.Where(z => z.Health > 0).GroupBy(z => z.Type).ToDictionary(g => g.Key, g => g.Count());
        if (!_eventsPrimed)
        {
            (_prevPlants, _prevZombieTypes, _eventsPrimed) = (curPlants, curTypes, true);
            return;
        }
        // One-shot plants (bombs, mines, squash...) are supposed to disappear, so ignore them.
        var lost = _prevPlants.Except(curPlants).Where(p => p.Item3 is not (2 or 4 or 14 or 15 or 17 or 20 or 27 or 47)).ToList();
        var newBig = curTypes.Where(kv => NotableZombies.Contains(kv.Key) &&
            kv.Value > (_prevZombieTypes.TryGetValue(kv.Key, out var o) ? o : 0)).Select(kv => kv.Key).ToList();
        (_prevPlants, _prevZombieTypes) = (curPlants, curTypes);

        if (now < _nextEvent) return;
        var alive = zombies.Where(z => z.Health > 0).ToList();

        if (alive.Count > 0 && now >= _nextBreach && alive.Min(z => z.X) < 330)
        {
            var row = alive.OrderBy(z => z.X).First().Row + 1;
            SayCategoryInterrupt("breach", new() { ["row"] = row.ToString() });
            _nextBreach = now.AddSeconds(14);
            _nextEvent = now.AddSeconds(4);
            return;
        }
        if (newBig.Count > 0)
        {
            var type = newBig[0];
            var z = alive.Where(x => x.Type == type).OrderByDescending(x => x.X).First();
            SayCategory("bigzombie", false, new() { ["zombie"] = ZombieName(type), ["row"] = (z.Row + 1).ToString() });
            _nextEvent = now.AddSeconds(5);
            return;
        }
        if (lost.Count > 0 && now - _lastShovel > TimeSpan.FromSeconds(2))
        {
            var p = lost[0];
            var name = Plants.FirstOrDefault(x => x.Id == p.Item3)?.Name ?? "plant";
            SayCategory("plantlost", false, new() { ["plant"] = name, ["row"] = (p.Item1 + 1).ToString() });
            _nextEvent = now.AddSeconds(6);
            return;
        }
        if (alive.Count >= 12 && now >= _nextHorde)
        {
            SayCategory("horde", false, new() { ["count"] = alive.Count.ToString() });
            _nextHorde = now.AddSeconds(25);
            _nextEvent = now.AddSeconds(5);
        }
    }

    // Never quiet: every 6-8 s of silence, say something that fits the moment.
    private void ContinuousYap()
    {
        UpdateStatsDisplay();
        _chat.Tick();
        if (VoiceCheck.IsChecked != true || DateTime.UtcNow < _nextFiller) return;
        try
        {
            var calm = false;
            if (_pvz.IsConnected && _pvz.ReadState() is { HasBoard: true, GameUi: 3 })
                calm = _pvz.ReadZombies().Count(z => z.Health > 0) < 3;
            if (_fillerBag.Count == 0)
            {
                string[] pool = ["bait", "welcome", "challenge", "score", "bait", "challenge"];
                foreach (var c in pool.OrderBy(_ => Random.Shared.Next())) _fillerBag.Enqueue(c);
            }
            var category = _fillerBag.Dequeue();
            if (calm && Random.Shared.Next(3) == 0) category = "calm";
            _nextFiller = DateTime.UtcNow.AddSeconds(6 + Random.Shared.NextDouble() * 2);
            SayCategory(category);
        }
        catch { }
    }

    private void UpdateStatsDisplay()
    {
        var text = _stats.Summary;
        if (StatsText.Text != text) StatsText.Text = text;
        // Score bar inside the game: shown whenever the bot is not speaking, refreshed so the timer ticks.
        var now = DateTime.UtcNow;
        if (now >= _speakingUntil && now >= _nextStatsPaint && _pvz.IsConnected && _lossSeenAt == DateTime.MinValue)
        {
            _nextStatsPaint = now.AddSeconds(2.5);
            try { _pvz.ShowGameMessage($"Bot lost: {_stats.Losses}  |  Time: {StreamStats.Clock(_stats.RunSeconds)}  |  Best: {StreamStats.Clock(_stats.BestSeconds)}", 7); }
            catch { }
        }
    }

    private static string StreamCategory(string effect) => effect switch
    {
        "PutZombie" or "ClearAllPlants" or "KillAllZombies" or "SetSun" => effect,
        "PutPlant" => "other", // chat can't plant per user, don't use PutPlant voice
        "Gift" or "gift" or "Donation" or "donation" => "other",
        "Follow" or "follow" or "Follower" => "other",
        "Like" or "like" or "Likes" => "other",
        "Share" or "share" => "other",
        _ => "other"
    };

    private static int JsonInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static int ResolveItem(JsonElement root, string name, IReadOnlyList<GameItem> items)
    {
        if (!root.TryGetProperty(name, out var value)) return items[Random.Shared.Next(items.Count)].Id;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        var text = value.GetString() ?? "";
        return items.FirstOrDefault(x => x.Name.Equals(text, StringComparison.OrdinalIgnoreCase))?.Id
               ?? items[Random.Shared.Next(items.Count)].Id;
    }

    private void RefreshState(bool silent = false)
    {
        if (!_pvz.IsConnected)
        {
            if (StatusText.Text.StartsWith("Connected", StringComparison.Ordinal)) SetDisconnected("Game process exited");
            return;
        }
        try
        {
            var state = _pvz.ReadState();
            if (state.HasBoard && state.Sun < 4_900_000)
            {
                _pvz.SetSun(5_000_000);
                state = state with { Sun = 5_000_000 };
            }
            var roundText = state.HasBoard && state.GameMode == 11
                ? $"   •   Endless round: {_pvz.ReadEndlessRounds()}" : "";
            StateText.Text = state.HasBoard
                ? $"Sun: {state.Sun}   •   UI: {DescribeUi(state.GameUi)}   •   Mode: {state.GameMode}   •   Scene: {DescribeScene(state.Scene)}{roundText}"
                : $"Game found   •   UI: {DescribeUi(state.GameUi)}   •   Board not loaded";
            if (!silent) Log($"State refreshed: {StateText.Text}");
        }
        catch (Exception ex)
        {
            if (!silent) ShowError("Refresh", ex);
        }
    }

    private (int Row, int Column) SelectedCell()
    {
        var row = int.Parse(((ComboBoxItem)RowChoice.SelectedItem).Tag!.ToString()!);
        var column = int.Parse(((ComboBoxItem)ColumnChoice.SelectedItem).Tag!.ToString()!);
        return (row, column);
    }

    private void StopBot()
    {
        _botRunning = false;
        _botTimer.Stop();
        BotStateText.Text = "Bot: stopped";
    }

    private void RunBotTick()
    {
        if (!_botRunning || _botBusy) return;
        _botBusy = true;
        try
        {
            var state = _pvz.ReadState();
            if (AutoMenu(state)) return;
            if (state.GameUi == 4)
            {
                if (_lossSeenAt == DateTime.MinValue)
                {
                    _lossSeenAt = DateTime.UtcNow;
                    _retryAttempts = 0;
                    Log("BOT detected a loss; waiting for the Try Again screen.");
                    var newBest = _stats.EndRunAsLoss();
                    _eventsPrimed = false;
                    _lossStep = 0;
                    SayCategoryInterrupt(newBest ? "newbest" : "loss");
                    LossCaption(newBest ? $"NEW BEST!  {StreamStats.Clock(_stats.LastRunSeconds)}" : $"THE BOT LOST!  Loss #{_stats.Losses}");
                }
                // A proper reaction over the loss screen: reaction now, then the score, then chat bait.
                var lossFor = (DateTime.UtcNow - _lossSeenAt).TotalSeconds;
                if (_lossStep == 0 && lossFor >= 3.5)
                {
                    _lossStep = 1; SayForce("lossstats");
                    LossCaption($"Lasted {StreamStats.Clock(_stats.LastRunSeconds)}  |  Best {StreamStats.Clock(_stats.BestSeconds)}");
                }
                else if (_lossStep == 1 && lossFor >= 7.0)
                {
                    _lossStep = 2; SayForce("lossbait");
                    string[] baits = ["Type GG in chat!", "Chat, send more zombies!", "Run it back, chat!", "Can you do it again?"];
                    LossCaption(baits[Random.Shared.Next(baits.Length)]);
                }
                BotStateText.Text = "Bot: waiting for Try Again";
                if (DateTime.UtcNow - _lossSeenAt >= TimeSpan.FromSeconds(11) &&
                    DateTime.UtcNow >= _nextRetryAttempt)
                {
                    var restarted = _pvz.TryAgainAfterLoss(_retryAttempts++);
                    _nextRetryAttempt = DateTime.UtcNow.AddSeconds(2);
                    if (restarted) Log("BOT pressed Try Again after a loss.");
                }
                return;
            }
            if (_lossSeenAt != DateTime.MinValue)
            {
                _lossSeenAt = DateTime.MinValue;
                _retryAttempts = 0;
                _seedChooserHandled = false;
            }
            if (state.HasBoard && state.GameUi == 2)
            {
                _pvz.SetBattleHudHidden(false);
                if (!_seedChooserHandled) _virtualArsenal.Reset();
                if (ContinuousEndlessCheck.IsChecked == true)
                {
                    _pvz.EnableStrictDayEndless();
                    state = _pvz.ReadState();
                }
                BotStateText.Text = "Bot: choosing useful plants";
                if (!_seedChooserHandled && AutoChooseCheck.IsChecked == true)
                {
                    _seedChooserHandled = _pvz.AutoChooseSeedsAndStart(state.Scene);
                    if (_seedChooserHandled)
                    {
                        Log($"BOT selected a {DescribeScene(state.Scene)} loadout and pressed Let's Rock."); SayCategory("start");
                        // Reset uninterrupted spawner for fresh endless battle
                        _spawner.Reset(DateTime.UtcNow);
                        _lastBattleRound = -1;
                    }
                    else
                        BotStateText.Text = "Bot: filling seed tray / retrying Let's Rock";
                }
                return;
            }
            _seedChooserHandled = false;
            if (!state.HasBoard || state.GameUi != 3)
            {
                BotStateText.Text = "Bot: waiting for Choose Your Plants or battle";
                return;
            }

            _pvz.SetBattleHudHidden(true);
            _stats.BeginRun();

            var uninterrupted = ContinuousEndlessCheck.IsChecked == true;
            if (uninterrupted)
            {
                // Real uninterrupted: suppress Survival stage transition, wave messages, and drive our own spawner
                _pvz.SuppressUninterruptedTransition();
                // Keep difficulty internal stage visible? We preserve EndlessRounds display but do not advance via chooser.
                // Spawner will run after zombie read below; reset if needed
                if (_spawner.ShouldReset) _spawner.Reset(DateTime.UtcNow);
            }
            else if (state.GameMode == 11)
            {
                var currentRound = _pvz.ReadEndlessRounds();
                if (_lastBattleRound >= 0 && currentRound > _lastBattleRound)
                {
                    _pvz.AdvanceSurvivalMessage();
                    Log($"BOT advanced Survival round {_lastBattleRound} → {currentRound}; preserved flag progress.");
                }
                _lastBattleRound = currentRound;
            }
            else _lastBattleRound = -1;

            if (state.Sun < 4_900_000)
            {
                _pvz.SetSun(5_000_000);
                state = state with { Sun = 5_000_000 };
            }

            // Read board once per tick for both spawner and bot
            var zombies = _pvz.ReadZombies();
            var plants = _pvz.ReadPlants();
            if (uninterrupted)
            {
                _spawner.Tick(_pvz, state, zombies, DateTime.UtcNow);
                // Re-read after potential spawn so bot sees fresh count (cheap extra read)
                zombies = _pvz.ReadZombies();
                plants = _pvz.ReadPlants();
            }
            var blocked = _pvz.ReadBlockedCells();
            if (blocked.Count != _lastBlockedCount)
            {
                _lastBlockedCount = blocked.Count;
                Log($"BOT sees {blocked.Count} unplantable tile(s) (craters/graves) and will skip them.");
            }
            var plantsForBot = blocked.Count == 0 ? plants
                : plants.Concat(blocked.Select(b => new PlantState(b.Row, b.Column, 99))).ToArray();

            if (DateTime.UtcNow < _nextBotAction) return;

            DetectEvents(zombies, plants);
            if (state.Scene == 0 && DateTime.UtcNow >= _nextCobFire && zombies.Count >= 6)
            {
                var cannon = plants.FirstOrDefault(p => p.Type == 47);
                if (cannon is not null)
                {
                    var cluster = zombies.GroupBy(z => z.Row).OrderByDescending(g => g.Count()).First();
                    var targetX = (int)cluster.Average(z => z.X);
                    _pvz.FireCobCannon(cannon.Row, cannon.Column, cluster.Key, targetX);
                    _nextCobFire = DateTime.UtcNow.AddSeconds(35);
                    Log($"BOT fired Cob Cannon at row {cluster.Key + 1}.");
                }
            }
            if (DateTime.UtcNow >= _nextRewardCollect)
            {
                _pvz.CollectLevelReward();
                _nextRewardCollect = DateTime.UtcNow.AddSeconds(2);
            }

            var virtualCards = _virtualArsenal.Available(state.Scene, DateTime.UtcNow);
            var cards = state.Scene == 0 ? virtualCards : _pvz.ReadSeedPackets();
            var action = _strategy.Choose(state, plantsForBot, zombies, cards);
            if (action is { UseShovel: false } && blocked.Any(b => b.Row == action.Row && b.Column == action.Column)) action = null;
            if (action is { UseShovel: true } && action.Type == 99) action = null;
            if (action is null)
            {
                BotStateText.Text = "Bot: monitoring — waiting for sun, cooldown, or danger";
                return;
            }

            var item = Plants.FirstOrDefault(p => p.Id == action.Type);
            if (item is not null) PlantChoice.SelectedItem = item;
            RowChoice.SelectedIndex = action.Row;
            ColumnChoice.SelectedIndex = action.Column;
            BotStateText.Text = $"Bot: {action.Reason} — {action.Name} at R{action.Row + 1} C{action.Column + 1}";
            if (action.UseShovel)
            {
                _lastShovel = DateTime.UtcNow;
                _pvz.ShovelPlant(action.Row, action.Column, action.Type);
                _nextBotAction = DateTime.UtcNow.AddMilliseconds(400);
                Log($"BOT shoveled {action.Name} at row {action.Row + 1}, column {action.Column + 1} ({action.Reason}).");
            }
            else
            {
                if (action.SeedIndex < 0)
                {
                    _pvz.PlacePlant(action.Row, action.Column, action.Type);
                    _virtualArsenal.Record(action.Type, DateTime.UtcNow);
                    if (action.Type == 47) _nextCobFire = DateTime.UtcNow.AddSeconds(35);
                    if (VirtualArsenal.NeedsCoffee(action.Type))
                    {
                        _pvz.PlacePlant(action.Row, action.Column, 35);
                        _virtualArsenal.Record(35, DateTime.UtcNow);
                    }
                }
                else _pvz.PlantUsingSeedCard(action.SeedIndex, action.Row, action.Column);
                _strategy.RecordAction(action, DateTime.UtcNow);
                _nextBotAction = DateTime.UtcNow.AddMilliseconds(action.Reason == "stop breach near house" ? 300 : 400);
                Log($"BOT placed {action.Name} → row {action.Row + 1}, column {action.Column + 1} ({action.Reason}).");
            }
        }
        catch (Exception ex)
        {
            _botRunning = true;
            BotStateText.Text = "Bot: recovering automatically";
            Log($"BOT recovered from: {ex.Message}");
        }
        finally
        {
            _botBusy = false;
        }
    }

    // Menu -> Survival Day (endless) -> Continue, with no human clicks. Each click is harmless on the other screens.
    private bool AutoMenu(GameState state)
    {
        var now = DateTime.UtcNow;
        if (_menuStage == 0)
        {
            if (state.HasBoard) { _noBoardSince = DateTime.MinValue; return false; }
            if (_noBoardSince == DateTime.MinValue) _noBoardSince = now;
            if (now - _noBoardSince < TimeSpan.FromSeconds(4)) return false;
            _menuStage = 1;
            _menuNext = now;
            Log("BOT is in the menus; starting Survival Endless by itself.");
        }
        BotStateText.Text = "Bot: opening Survival Endless";
        if (now < _menuNext) return true;
        switch (_menuStage)
        {
            case 1: _pvz.ClickClient(267, 557); _menuNext = now.AddSeconds(2.5); break; // Click to start
            case 2: _pvz.ClickClient(547, 402); _menuNext = now.AddSeconds(2); break;   // Survival
            case 3: _pvz.ClickClient(87, 172); _menuNext = now.AddSeconds(3); break;    // Survival: Day
            case 4: _pvz.ClickClient(279, 369); _menuNext = now.AddSeconds(3); break;   // Continue
            default: _menuStage = 0; _noBoardSince = DateTime.MinValue; return false;
        }
        _menuStage++;
        return true;
    }

    private void SetDisconnected(string text)
    {
        StatusText.Text = text;
        StatusText.Foreground = Brushes.Orange;
        StateText.Text = "Start PvZ, then connect.";
    }

    private void Run(string action, Action body, Action? rollbackUi = null)
    {
        try { body(); }
        catch (Exception ex)
        {
            rollbackUi?.Invoke();
            ShowError(action, ex);
        }
    }

    private void ShowError(string action, Exception ex)
    {
        Log($"ERROR — {action}: {ex.Message}");
        MessageBox.Show(this, ex.Message, action, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        if (LogBox.Text.Length > 20_000)
        {
            var cut = LogBox.Text.IndexOf('\n', LogBox.Text.Length - 16_000);
            LogBox.Text = cut >= 0 ? LogBox.Text[(cut + 1)..] : LogBox.Text[^16_000..];
        }
        LogBox.ScrollToEnd();
    }

    private static string DescribeUi(int ui) => ui switch { 2 => "Seed selection", 3 => "Battle", _ => ui.ToString() };
    private static string DescribeScene(int scene) => scene switch { 0 => "Day", 1 => "Night", 2 => "Pool", 3 => "Fog", 4 => "Roof", 5 => "Moon", _ => scene.ToString() };

}
