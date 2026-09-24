using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace PvZController;

internal sealed class PvzProcess : IDisposable
{
    private const uint Access = NativeMethods.ProcessCreateThread | NativeMethods.ProcessVmOperation |
                                NativeMethods.ProcessVmRead | NativeMethods.ProcessVmWrite |
                                NativeMethods.ProcessQueryLimitedInformation;
    private readonly Dictionary<uint, byte[]> _ownedPatches = [];
    private Process? _process;
    private nint _handle;
    private nint _window;
    private int? _originalFreePlanting;
    private uint _hiddenHudBoard;
    private uint _hiddenHudBank;
    private int _originalBankY;
    private byte _originalShowShovel;

    internal nint GameWindow => _window;
    internal bool IsConnected => _process is { HasExited: false } && _handle != 0;
    internal string? ExecutablePath { get; private set; }

    internal void Connect()
    {
        Disconnect();
        var candidates = Process.GetProcessesByName("PlantsVsZombies")
            .Where(p => !p.HasExited)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("PvZ is not running. Start the game first.");

        foreach (var candidate in candidates)
        {
            try
            {
                var path = candidate.MainModule?.FileName;
                if (path is null || !VerifyExecutable(path)) continue;
                var handle = NativeMethods.OpenProcess(Access, false, (uint)candidate.Id);
                if (handle == 0) throw Win32("PvZ was found but could not be opened");
                _process = candidate;
                _handle = handle;
                _window = NativeMethods.FindWindow("MainWindow", null);
                if (_window == 0 || NativeMethods.GetWindowThreadProcessId(_window, out var windowPid) == 0 || windowPid != (uint)candidate.Id)
                    throw new InvalidOperationException("PvZ was found, but its game window could not be located.");
                ExecutablePath = path;
                ValidateRuntime();
                return;
            }
            catch
            {
                candidate.Dispose();
                if (_handle != 0) NativeMethods.CloseHandle(_handle);
                _handle = 0;
                _process = null;
            }
        }
        throw new InvalidOperationException("A PlantsVsZombies process was found, but it does not match the verified 1.2.0.1073 executable.");
    }

    internal GameState ReadState()
    {
        EnsureConnected();
        var lawn = ReadUInt32(Pvz1073Profile.Lawn);
        if (lawn == 0) return new GameState(0, 0, -1, -1, false);
        var ui = ReadInt32(lawn + Pvz1073Profile.GameUi);
        var mode = ReadInt32(lawn + Pvz1073Profile.GameMode);
        var board = ReadUInt32(lawn + Pvz1073Profile.Board);
        if (board == 0) return new GameState(0, ui, mode, -1, false);
        return new GameState(ReadInt32(board + Pvz1073Profile.Sun), ui, mode,
            ReadInt32(board + Pvz1073Profile.Scene), true);
    }

    internal void SetSun(int value)
    {
        if (value is < 0 or > 9999999) throw new ArgumentOutOfRangeException(nameof(value), "Sun must be between 0 and 9,999,999.");
        var board = RequireBoard();
        WriteInt32(board + Pvz1073Profile.Sun, value);
    }

    internal void EnableStrictDayEndless()
    {
        if (ReadState().GameUi != 2) return;
        var lawn = RequireLawn();
        var board = RequireBoard();
        var scene = ReadInt32(board + Pvz1073Profile.Scene);
        if (scene != 0) return;
        WriteInt32(lawn + Pvz1073Profile.GameMode, 11);
        var blocks = new int[54];
        for (var column = 0; column < 9; column++)
            for (var row = 0; row < 6; row++)
                blocks[column * 6 + row] = row < 5 ? 1 : 2;
        Write(board + Pvz1073Profile.BlockType, blocks.SelectMany(BitConverter.GetBytes).ToArray());
        Write(board + Pvz1073Profile.RowType, new[] { 1,1,1,1,1,0 }.SelectMany(BitConverter.GetBytes).ToArray());
        var challenge = ReadUInt32(board + Pvz1073Profile.Challenge);
        if (challenge != 0 && ReadInt32(challenge + Pvz1073Profile.EndlessRounds) <= 0)
            WriteInt32(challenge + Pvz1073Profile.EndlessRounds, 1);
    }

    internal int ReadEndlessRounds()
    {
        var board = RequireBoard();
        var challenge = ReadUInt32(board + Pvz1073Profile.Challenge);
        return challenge == 0 ? 0 : Math.Max(0, ReadInt32(challenge + Pvz1073Profile.EndlessRounds));
    }

    internal void AdvanceSurvivalMessage()
    {
        if (_window == 0) return;
        NativeMethods.PostMessageW(_window, NativeMethods.WmKeyDown, 0x0D, 0);
        NativeMethods.PostMessageW(_window, NativeMethods.WmKeyUp, 0x0D, 0);
    }

    internal IReadOnlyList<PlantState> ReadPlants()
    {
        var board = RequireBoard();
        var array = ReadUInt32(board + Pvz1073Profile.PlantArray);
        var count = ReadUInt32(board + Pvz1073Profile.PlantCountMax);
        if (array == 0 || count > 1024) return [];

        var plants = new List<PlantState>();
        if (count == 0) return plants;
        var snapshot = Read(array, checked((int)(count * Pvz1073Profile.PlantStructSize)));
        for (uint i = 0; i < count; i++)
        {
            var start = checked((int)(i * Pvz1073Profile.PlantStructSize));
            var item = snapshot.AsSpan(start, (int)Pvz1073Profile.PlantStructSize);
            if (item[(int)Pvz1073Profile.PlantDead] != 0 ||
                item[(int)Pvz1073Profile.PlantSquished] != 0) continue;
            var row = BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.PlantRow, 4));
            var column = BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.PlantColumn, 4));
            var type = BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.PlantType, 4));
            if (row is >= 0 and < 6 && column is >= 0 and < 9 && type is >= 0 and <= 47)
                plants.Add(new PlantState(row, column, type));
        }
        return plants;
    }

    internal IReadOnlyList<ZombieState> ReadZombies()
    {
        var board = RequireBoard();
        var array = ReadUInt32(board + Pvz1073Profile.ZombieArray);
        var count = ReadUInt32(board + Pvz1073Profile.ZombieCountMax);
        if (array == 0 || count > 2048) return [];
        var zombies = new List<ZombieState>();
        if (count == 0) return zombies;
        var snapshot = Read(array, checked((int)(count * Pvz1073Profile.ZombieStructSize)));
        for (uint i = 0; i < count; i++)
        {
            var start = checked((int)(i * Pvz1073Profile.ZombieStructSize));
            var item = snapshot.AsSpan(start, (int)Pvz1073Profile.ZombieStructSize);
            if (item[(int)Pvz1073Profile.ZombieDead] != 0) continue;
            var row = BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.ZombieRow, 4));
            var type = BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.ZombieType, 4));
            var x = BitConverter.ToSingle(item.Slice((int)Pvz1073Profile.ZombieX, 4));
            var health = Math.Max(0, BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.ZombieHealth, 4))) +
                         Math.Max(0, BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.ZombieHelmHealth, 4))) +
                         Math.Max(0, BitConverter.ToInt32(item.Slice((int)Pvz1073Profile.ZombieShieldHealth, 4)));
            if (row is >= 0 and < 6 && type is >= 0 and <= 32 && x is > -100 and < 1200 && health > 0)
                zombies.Add(new ZombieState(row, type, x, health));
        }
        return zombies;
    }

    internal IReadOnlyList<SeedPacketState> ReadSeedPackets()
    {
        var bank = ReadUInt32(RequireBoard() + Pvz1073Profile.SeedBank);
        if (bank == 0) return [];
        var count = ReadInt32(bank + Pvz1073Profile.SeedCount);
        if (count is < 1 or > 10) return [];
        var result = new List<SeedPacketState>(count);
        for (var i = 0; i < count; i++)
        {
            var packet = bank + (uint)i * Pvz1073Profile.SeedPacketSize;
            var type = ReadInt32(packet + Pvz1073Profile.SeedType);
            if (type == 48) type = ReadInt32(packet + Pvz1073Profile.SeedImitaterType);
            var past = ReadInt32(packet + Pvz1073Profile.SeedCooldownPast);
            var total = ReadInt32(packet + Pvz1073Profile.SeedCooldownTotal);
            var refreshing = ReadByte(packet + Pvz1073Profile.SeedRefreshing) != 0;
            if (type is >= 0 and <= 47) result.Add(new SeedPacketState(i, type, past, total, !refreshing));
        }
        return result;
    }

    internal int ReadSeedCapacity()
    {
        var bank = ReadUInt32(RequireBoard() + Pvz1073Profile.SeedBank);
        if (bank == 0) return 0;
        return Math.Clamp(ReadInt32(bank + Pvz1073Profile.SeedCount), 0, 10);
    }

    internal IReadOnlyList<string> ReadSeedBankHeader()
    {
        var bank = ReadUInt32(RequireBoard() + Pvz1073Profile.SeedBank);
        if (bank == 0) return [];
        return Enumerable.Range(0, 36)
            .Select(i => $"0x{i * 4:X2}:{ReadInt32(bank + (uint)(i * 4))}").ToArray();
    }

    internal string ReadHudDebug()
    {
        var board = RequireBoard();
        return $"SeedBankY={ReadInt32(ReadUInt32(board + Pvz1073Profile.SeedBank) + 0x0C)}, " +
               $"Board5600={Convert.ToHexString(Read(board + 0x5600, 32))}";
    }

    internal void SetBattleHudHidden(bool hidden)
    {
        if (!IsConnected) return;
        var state = ReadState();
        var board = state.HasBoard ? RequireBoard() : 0;
        if (_hiddenHudBoard != 0 && (!hidden || board != _hiddenHudBoard))
        {
            if (board == _hiddenHudBoard && ReadUInt32(board + Pvz1073Profile.SeedBank) == _hiddenHudBank)
            {
                WriteInt32(_hiddenHudBank + Pvz1073Profile.WidgetY, _originalBankY);
                Write(board + Pvz1073Profile.BoardShowShovel, [_originalShowShovel]);
            }
            _hiddenHudBoard = 0;
        }
        if (!hidden || state.GameUi != 3 || board == 0) return;
        var bank = ReadUInt32(board + Pvz1073Profile.SeedBank);
        if (bank == 0) return;
        if (_hiddenHudBoard == 0)
        {
            var y = ReadInt32(bank + Pvz1073Profile.WidgetY);
            var shovel = ReadByte(board + Pvz1073Profile.BoardShowShovel);
            if (y is < -120 or > 100 || shovel is not (0 or 1))
                throw new InvalidOperationException("PvZ HUD layout did not match the verified build.");
            _hiddenHudBoard = board;
            _hiddenHudBank = bank;
            _originalBankY = y == -100 ? 0 : y;
            _originalShowShovel = shovel;
        }
        WriteInt32(bank + Pvz1073Profile.WidgetY, -100);
        Write(board + Pvz1073Profile.BoardShowShovel, [0]);
        // Also suppress the bottom wave flag meter for uninterrupted mode.
        try { WriteInt32(board + Pvz1073Profile.BoardProgressMeterWidth, 0); } catch { }
        try { WriteInt32(board + Pvz1073Profile.BoardFlagRaiseCounter, 0); } catch { }
    }

    internal void SuppressUninterruptedTransition()
    {
        if (!IsConnected) return;
        try
        {
            var board = RequireBoard();
            // Keep the level from ever signalling complete and from fading to the seed chooser.
            WriteInt32(board + Pvz1073Profile.BoardNextSurvivalStageCounter, 0);
            WriteInt32(board + Pvz1073Profile.BoardBoardFadeOutCounter, -1);
            Write(board + Pvz1073Profile.BoardLevelComplete, [0]);
            Write(board + Pvz1073Profile.BoardLevelAwardSpawned, [0]);
            // Prevent the engine's own wave timer from spawning or triggering Huge Wave advice.
            WriteInt32(board + Pvz1073Profile.BoardZombieCountDown, 7500);
            WriteInt32(board + Pvz1073Profile.BoardZombieCountDownStart, 7500);
            WriteInt32(board + Pvz1073Profile.BoardHugeWaveCountDown, 9999);
            WriteInt32(board + Pvz1073Profile.BoardProgressMeterWidth, 0);
            WriteInt32(board + Pvz1073Profile.BoardFlagRaiseCounter, 0);
            // Hide bottom-right "Survival Day Endless X flags completed" text
            // by nuking the wave counts and survival stage display.
            WriteInt32(board + Pvz1073Profile.BoardNumWaves, 0);
            try
            {
                var challenge = ReadUInt32(board + Pvz1073Profile.Challenge);
                if (challenge != 0)
                {
                    // mSurvivalStage display - set to 0 to hide "2 flags completed"
                    WriteInt32(challenge + Pvz1073Profile.EndlessRounds, 0);
                    // Also try to kill any flag meter text offset nearby (extra safety)
                    // The flag text is derived from these, so zeroing is enough.
                }
            }
            catch { }
            // Keep wave index stable so the game never thinks it reached the last wave.
            var cur = ReadInt32(board + Pvz1073Profile.BoardCurrentWave);
            if (cur != 0) WriteInt32(board + Pvz1073Profile.BoardCurrentWave, 0);
        }
        catch { }
    }

    internal IReadOnlyList<string> ScanSeedChooserCandidates()
    {
        var lawn = RequireLawn();
        var found = new List<string>();
        for (uint offset = 0x700; offset <= 0x920; offset += 4)
        {
            try
            {
                var pointer = ReadUInt32(lawn + offset);
                if (pointer < 0x10000) continue;
                for (uint rel = 0xC00; rel <= 0xE80; rel += 4)
                {
                    var needed = ReadInt32(pointer + rel);
                    if (needed is < 1 or > 10) continue;
                    var inBank = ReadInt32(pointer + rel + 0xC);
                    var state = ReadInt32(pointer + rel + 0x20);
                    if (inBank is >= 0 and <= 10 && state is >= 0 and <= 4)
                        found.Add($"lawn+0x{offset:X}=0x{pointer:X8}, rel=0x{rel:X}, needed={needed}, inBank={inBank}, state={state}");
                }
            }
            catch { }
        }
        return found;
    }

    internal uint ReadSeedChooserPointer() => ReadUInt32(RequireLawn() + Pvz1073Profile.SeedChooser);

    internal IReadOnlyList<string> ReadChooserSeedDebug()
    {
        var chooser = ReadSeedChooserPointer();
        if (chooser == 0) return [];
        var result = new List<string>();
        for (var type = 0; type < 48; type++)
        {
            var address = chooser + Pvz1073Profile.ChosenSeeds + (uint)type * Pvz1073Profile.ChosenSeedSize;
            var values = Enumerable.Range(0, 15).Select(i => ReadInt32(address + (uint)(i * 4))).ToArray();
            if (values.Any(v => v != 0 && v != -1)) result.Add($"T{type}:" + string.Join(',', values));
        }
        return result;
    }

    internal IReadOnlyList<int> ReadChooserSelectedTypes()
    {
        var chooser = ReadSeedChooserPointer();
        if (chooser == 0) return [];
        var result = new List<int>();
        for (var type = 0; type < 48; type++)
        {
            var address = chooser + Pvz1073Profile.ChosenSeeds + (uint)type * Pvz1073Profile.ChosenSeedSize;
            // +0x24 is PvZ's chooser-card state: 1 means selected/landing in
            // the top tray, while 3 means available in the chooser grid.
            if (ReadInt32(address + 0x24) == 1) result.Add(type);
        }
        return result;
    }

    internal IReadOnlyList<string> ScanChooserBoardLinks()
    {
        var chooser = ReadSeedChooserPointer();
        var board = RequireBoard();
        var result = new List<string>();
        if (chooser == 0) return result;
        for (uint rel = 0; rel <= 0xF00; rel += 4)
            try { if (ReadUInt32(chooser + rel) == board) result.Add($"0x{rel:X}"); } catch { }
        return result;
    }

    internal IReadOnlyList<uint> ReadChooserVtable()
    {
        var chooser = ReadSeedChooserPointer();
        if (chooser == 0) return [];
        var table = ReadUInt32(chooser);
        return Enumerable.Range(0, 80).Select(i => ReadUInt32(table + (uint)(i * 4))).ToArray();
    }

    internal void PlantUsingSeedCard(int seedIndex, int row, int column)
    {
        EnsureUnpaused();
        ValidateCell(row, column);
        if (ReadState().GameUi != 3) throw new InvalidOperationException("Normal planting requires an active battle.");
        var count = ReadSeedPackets().Count;
        if (seedIndex < 0 || seedIndex >= count) throw new ArgumentOutOfRangeException(nameof(seedIndex));
        var x = count switch { <= 7 => seedIndex * 59 + 110, 8 => seedIndex * 54 + 106, 9 => seedIndex * 52 + 105, _ => seedIndex * 51 + 104 };
        ClickGameMessage(x, 43);
        Thread.Sleep(35);
        var tileX = column * 80 + 80;
        var tileY = row * (ReadState().Scene is 2 or 3 or 4 ? 85 : 100) + 130;
        if (ReadState().Scene == 4 && column < 5) tileY += (5 - column) * 20 - 10;
        ClickGameMessage(tileX, tileY);
    }

    internal void FireCobCannon(int row, int column, int targetRow, int targetX)
    {
        if (ReadState().GameUi != 3 || ReadState().Scene != 0) return;
        ClickGameMessage(120 + column * 80, 130 + row * 100);
        Thread.Sleep(350);
        ClickGameMessage(Math.Clamp(targetX, 250, 760), 130 + targetRow * 100);
    }

    internal void ShovelPlant(int row, int column, int? type = null)
    {
        ValidateCell(row, column);
        if (ReadState().GameUi != 3) throw new InvalidOperationException("Shoveling requires an active battle.");
        var board = RequireBoard();
        var array = ReadUInt32(board + Pvz1073Profile.PlantArray);
        var count = Math.Min(ReadUInt32(board + Pvz1073Profile.PlantCountMax), 1024);
        for (uint i = 0; i < count; i++)
        {
            var address = array + i * Pvz1073Profile.PlantStructSize;
            if (ReadByte(address + Pvz1073Profile.PlantDead) != 0 ||
                ReadInt32(address + Pvz1073Profile.PlantRow) != row ||
                ReadInt32(address + Pvz1073Profile.PlantColumn) != column ||
                type is not null && ReadInt32(address + Pvz1073Profile.PlantType) != type) continue;
            Write(address + Pvz1073Profile.PlantDead, [1]);
            return;
        }
    }

    internal void CollectLevelReward()
    {
        if (ReadState().GameUi == 3) ClickGameMessage(400, 260);
    }

    internal bool TryAgainAfterLoss(int attempt)
    {
        if (ReadState().GameUi != 4) return false;
        NativeMethods.PostMessageW(_window, NativeMethods.WmKeyDown, 0x0D, 0);
        NativeMethods.PostMessageW(_window, NativeMethods.WmKeyUp, 0x0D, 0);
        Thread.Sleep(150);
        if (ReadState().GameUi != 4) return true;
        // The centered dialog changes height with its Survival result text.
        // Stop as soon as the scene changes so later clicks cannot touch the
        // next screen's seed chooser.
        for (var y = 560; y >= 235; y -= 25)
        {
            if (ReadState().GameUi != 4) return true;
            ClickGameMessage(400, y);
            Thread.Sleep(120);
        }
        return ReadState().GameUi != 4;
    }

    internal bool AutoChooseSeedsAndStart(int scene)
    {
        if (ReadState().GameUi != 2) return false;
        EnsureUnpaused();
        if (ReadSeedChooserPointer() == 0) return false;
        int[] preferred = scene switch
        {
            1 => [26,27,8,10,13,15,14,17,2,20,23,3],
            2 => [16,26,27,39,18,7,5,17,2,20,23,3],
            3 => [16,26,27,8,10,5,7,17,2,20,23,3],
            4 => [33,26,27,39,32,34,7,5,2,20,17,23],
            _ => [26,27,39,7,5,18,2,20,17,23,3,4]
        };
        var capacity = ReadSeedCapacity();
        var selected = ReadChooserSelectedTypes();
        if (capacity > 0 && selected.Count >= capacity) return StartSelectedSeeds();
        // The chooser rejects further cards when its selected-count cache is
        // stale (for example after an interrupted internal selection). Rebuild
        // it from the card states before asking PvZ to choose more cards.
        WriteInt32(ReadSeedChooserPointer() + 0xD3C, selected.Count);
        var combo = preferred.Concat(Enumerable.Range(0, 48)).Distinct()
            .Where(type => !selected.Contains(type)).ToArray();
        ChooseSeedComboDirect(combo);
        Thread.Sleep(60);
        if (ReadChooserSelectedTypes().Count != capacity)
        {
            foreach (var type in preferred.Concat(Enumerable.Range(0, 48)).Distinct())
            {
                var now = ReadChooserSelectedTypes();
                if (now.Count >= capacity) break;
                if (now.Contains(type)) continue;
                // Card centers in PvZ's fixed 800x600 client area. PostMessage
                // targets only the game window and never moves the real cursor.
                var x = 50 + type % 8 * 53;
                var y = 150 + type / 8 * 70;
                ClickGameMessage(x, y);
                Thread.Sleep(35);
            }
        }
        if (ReadChooserSelectedTypes().Count != capacity) return false;
        return StartSelectedSeeds();
    }

    private bool StartSelectedSeeds()
    {
        // Survival Endless uses the same chooser after every two flags. Advance
        // it immediately; keep using PvZ's own button routine so saving and
        // the next wave setup remain intact.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && ReadState().GameUi == 2)
        {
            PressLetsRockDirect();
            Thread.Sleep(100);
            if (ReadState().GameUi != 2) break;
            ClickGameMessage(235, 555);
            Thread.Sleep(100);
        }
        return ReadState().GameUi == 3;
    }

    private void ClearSeedComboDirect()
    {
        var chooser = ReadSeedChooserPointer();
        if (chooser == 0) return;
        var code = new X86Builder();
        // Survival Endless retains chooser-card state between flags even while the
        // visible seed bank is empty. Let PvZ clear every possible retained card;
        // its own unchoose routine ignores cards that are already unselected.
        foreach (var type in Enumerable.Range(0, 40).Reverse())
        {
            var chosen = chooser + Pvz1073Profile.ChosenSeeds + (uint)type * Pvz1073Profile.ChosenSeedSize;
            code.MovRegisterImmediate(0, checked((int)chooser));
            code.Push(checked((int)chosen));
            code.Call(Pvz1073Profile.CallUnchooseSeed);
            code.MovRegisterImmediate(0, checked((int)chosen));
            code.MovRegisterImmediate(1, checked((int)chooser));
            code.Call(Pvz1073Profile.CallLandChosenSeed);
        }
        code.Return();
        Execute(code);
    }

    private void ChooseSeedComboDirect(IEnumerable<int> types)
    {
        var chooser = ReadSeedChooserPointer();
        if (chooser == 0) return;
        var code = new X86Builder();
        foreach (var type in types)
        {
            var chosenSeed = chooser + Pvz1073Profile.ChosenSeeds + (uint)type * Pvz1073Profile.ChosenSeedSize;
            code.MovRegisterImmediate(0, checked((int)chooser)); // eax = chooser
            code.Push(checked((int)chosenSeed));
            code.Call(Pvz1073Profile.CallChooseSeed);
            code.MovRegisterImmediate(0, checked((int)chosenSeed)); // eax = chosen seed
            code.MovRegisterImmediate(1, checked((int)chooser)); // ecx = chooser
            code.Call(Pvz1073Profile.CallLandChosenSeed);
        }
        code.Return();
        Execute(code);
    }

    private void PressLetsRockDirect()
    {
        var chooser = ReadSeedChooserPointer();
        if (chooser == 0) return;
        var code = new X86Builder();
        code.MovRegisterImmediate(1, checked((int)chooser)); // ecx = chooser
        code.Push(Pvz1073Profile.LetsRockButtonId);
        code.Call(Pvz1073Profile.CallChooserButton);
        code.Return();
        Execute(code);
    }

    internal bool IsPaused()
    {
        var state = ReadState();
        return state.HasBoard && ReadByte(RequireBoard() + Pvz1073Profile.GamePaused) != 0;
    }

    internal void EnsureUnpaused()
    {
        if (!IsPaused()) return;
        NativeMethods.PostMessageW(_window, NativeMethods.WmKeyDown, NativeMethods.VkSpace, 0);
        Thread.Sleep(30);
        NativeMethods.PostMessageW(_window, NativeMethods.WmKeyUp, NativeMethods.VkSpace, 0);
        Thread.Sleep(200);
    }

    private void ClickGameMessage(int x, int y)
    {
        if (_window == 0) throw new InvalidOperationException("PvZ window is unavailable.");
        var lp = (nint)((y << 16) | (x & 0xFFFF));
        NativeMethods.PostMessageW(_window, NativeMethods.WmMouseMove, 0, lp);
        if (!NativeMethods.PostMessageW(_window, NativeMethods.WmLButtonDown, NativeMethods.MkLButton, lp))
            throw Win32("Could not send chooser input to PvZ");
        Thread.Sleep(30);
        if (!NativeMethods.PostMessageW(_window, NativeMethods.WmLButtonUp, 0, lp))
            throw Win32("Could not release chooser input in PvZ");
    }

    internal void SetFreePlanting(bool enabled)
    {
        var lawn = RequireLawn();
        var address = lawn + Pvz1073Profile.FreePlanting;
        if (enabled)
        {
            _originalFreePlanting ??= ReadInt32(address);
            WriteInt32(address, 1);
        }
        else if (_originalFreePlanting is int original)
        {
            WriteInt32(address, original);
            _originalFreePlanting = null;
        }
    }

    internal void SetAutoCollect(bool enabled) => SetOwnedPatch(
        Pvz1073Profile.AutoCollected,
        [Pvz1073Profile.AutoCollectedOriginal],
        [Pvz1073Profile.AutoCollectedEnabled], enabled);

    internal void SetBackgroundRunning(bool enabled) => SetOwnedPatch(
        Pvz1073Profile.BackgroundRunning,
        Pvz1073Profile.BackgroundRunningOriginal,
        Pvz1073Profile.BackgroundRunningEnabled, enabled);

    internal void PlacePlant(int row, int column, int type)
    {
        ValidateCell(row, column);
        RequireBattle();
        var code = new X86Builder();
        code.Push(-1);
        code.Push(type);
        code.MovRegisterImmediate(0, row); // eax
        code.Push(column);
        code.MovRegisterFromAbsolute(5, Pvz1073Profile.Lawn); // ebp
        code.MovRegisterFromOffset(5, Pvz1073Profile.Board);
        code.PushRegister(5);
        code.Call(Pvz1073Profile.CallPutPlant);
        code.Return();
        Execute(code);
    }

    internal void SpawnZombie(int row, int column, int type)
    {
        ValidateCell(row, column);
        RequireBattle();
        if (type == 25) throw new InvalidOperationException("Dr. Zomboss needs a separate placement path and is not enabled in this milestone.");
        var code = new X86Builder();
        code.Push(column);
        code.Push(type);
        code.MovRegisterImmediate(0, row); // eax
        code.MovRegisterFromAbsolute(1, Pvz1073Profile.Lawn); // ecx
        code.MovRegisterFromOffset(1, Pvz1073Profile.Board);
        code.MovRegisterFromOffset(1, Pvz1073Profile.Challenge);
        code.Call(Pvz1073Profile.CallPutZombie);
        code.Return();
        Execute(code);
    }

    internal void KillAllZombies()
    {
        var board = RequireBoard();
        var array = ReadUInt32(board + Pvz1073Profile.ZombieArray);
        var count = Math.Min(ReadUInt32(board + Pvz1073Profile.ZombieCountMax), 2048);
        for (uint i = 0; i < count; i++)
        {
            var address = array + i * Pvz1073Profile.ZombieStructSize;
            if (ReadByte(address + Pvz1073Profile.ZombieDead) == 0)
                WriteInt32(address + Pvz1073Profile.ZombieHealth, 0);
        }
    }

    internal void ClearAllPlants()
    {
        var board = RequireBoard();
        var array = ReadUInt32(board + Pvz1073Profile.PlantArray);
        var count = Math.Min(ReadUInt32(board + Pvz1073Profile.PlantCountMax), 1024);
        for (uint i = 0; i < count; i++)
        {
            var address = array + i * Pvz1073Profile.PlantStructSize;
            if (ReadByte(address + Pvz1073Profile.PlantDead) == 0)
                Write(address + Pvz1073Profile.PlantDead, [1]);
        }
    }

    internal void RestoreOwnedChanges()
    {
        if (!IsConnected) return;
        try { SetBattleHudHidden(false); } catch { }
        foreach (var item in _ownedPatches.ToArray())
        {
            TryWrite(item.Key, item.Value);
            _ownedPatches.Remove(item.Key);
        }
        try { SetFreePlanting(false); } catch { }
        TryWrite(Pvz1073Profile.BlockMainLoop, [Pvz1073Profile.BlockMainLoopOriginal]);
    }

    internal void Disconnect()
    {
        RestoreOwnedChanges();
        if (_handle != 0) NativeMethods.CloseHandle(_handle);
        _handle = 0;
        _window = 0;
        _process?.Dispose();
        _process = null;
        ExecutablePath = null;
        _ownedPatches.Clear();
        _originalFreePlanting = null;
    }

    public void Dispose() => Disconnect();

    private void Execute(X86Builder builder)
    {
        EnsureConnected();
        VerifyBytes(Pvz1073Profile.BlockMainLoop, [Pvz1073Profile.BlockMainLoopOriginal], "main-loop guard");
        Write(Pvz1073Profile.BlockMainLoop, [Pvz1073Profile.BlockMainLoopEnabled]);
        var thread = nint.Zero;
        var remote = nint.Zero;
        var completed = false;
        try
        {
            var frame = Math.Clamp(ReadInt32(RequireLawn() + Pvz1073Profile.FrameDuration), 1, 100);
            Thread.Sleep(frame * 2);
            remote = NativeMethods.VirtualAllocEx(_handle, 0, 4096, NativeMethods.MemCommit, NativeMethods.PageExecuteReadWrite);
            if (remote == 0) throw Win32("Could not allocate command memory in PvZ");
            var remote32 = checked((uint)remote.ToInt64());
            var code = builder.Build(remote32);
            Write(remote32, code);
            thread = NativeMethods.CreateRemoteThread(_handle, 0, 0, remote, 0, 0, out _);
            if (thread == 0) throw Win32("Could not start the PvZ command");
            completed = NativeMethods.WaitForSingleObject(thread, 10000) == NativeMethods.WaitObject0;
            if (!completed) throw new TimeoutException("PvZ did not finish the command within ten seconds. The allocated command buffer was retained for safety.");
        }
        finally
        {
            if (thread != 0) NativeMethods.CloseHandle(thread);
            if (completed && remote != 0) NativeMethods.VirtualFreeEx(_handle, remote, 0, NativeMethods.MemRelease);
            TryWrite(Pvz1073Profile.BlockMainLoop, [Pvz1073Profile.BlockMainLoopOriginal]);
        }
    }

    private void SetOwnedPatch(uint address, byte[] expected, byte[] enabled, bool on)
    {
        if (on)
        {
            var actual = Read(address, expected.Length);
            if (actual.SequenceEqual(enabled))
            {
                // A previous controller process may have exited while this reversible
                // patch was active. Adopt it so reconnect is safe and Disconnect can
                // still restore the verified original bytes.
                _ownedPatches.TryAdd(address, expected);
                return;
            }
            if (!actual.SequenceEqual(expected))
                throw new InvalidOperationException($"Refused patch guard: expected {Convert.ToHexString(expected)} or already-enabled {Convert.ToHexString(enabled)} at 0x{address:X8}, found {Convert.ToHexString(actual)}. Another mod or an unexpected build may be active.");
            _ownedPatches[address] = expected;
            Write(address, enabled);
        }
        else if (_ownedPatches.Remove(address, out var original))
        {
            Write(address, original);
        }
        else
        {
            VerifyBytes(address, expected, "patch state");
        }
    }

    private void ValidateRuntime()
    {
        VerifyBytes(Pvz1073Profile.BlockMainLoop, [Pvz1073Profile.BlockMainLoopOriginal], "main-loop signature");
        var lawn = ReadUInt32(Pvz1073Profile.Lawn);
        if (lawn != 0 && lawn < 0x10000) throw new InvalidOperationException("PvZ runtime pointers are invalid.");
    }

    private static bool VerifyExecutable(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!hash.Equals(Pvz1073Profile.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) return false;
        stream.Position = 0x3C;
        Span<byte> dword = stackalloc byte[4];
        stream.ReadExactly(dword);
        var pe = BitConverter.ToUInt32(dword);
        stream.Position = pe + 8;
        stream.ReadExactly(dword);
        return BitConverter.ToUInt32(dword) == Pvz1073Profile.ExpectedPeTimestamp;
    }

    private uint RequireLawn()
    {
        var lawn = ReadUInt32(Pvz1073Profile.Lawn);
        return lawn != 0 ? lawn : throw new InvalidOperationException("PvZ has not initialized its main state yet.");
    }

    private uint RequireBoard()
    {
        var board = ReadUInt32(RequireLawn() + Pvz1073Profile.Board);
        return board != 0 ? board : throw new InvalidOperationException("Enter seed selection or a battle first.");
    }

    private void RequireBattle()
    {
        var state = ReadState();
        if (!state.HasBoard || state.GameUi is not (2 or 3))
            throw new InvalidOperationException("Enter seed selection or a battle before placing an entity.");
    }

    private void ValidateCell(int row, int column)
    {
        var scene = ReadState().Scene;
        var rows = scene is 2 or 3 ? 6 : 5;
        if (row < 0 || row >= rows) throw new ArgumentOutOfRangeException(nameof(row), $"This scene has {rows} rows.");
        if (column is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(column));
    }

    private int ReadInt32(uint address) => BitConverter.ToInt32(Read(address, 4));
    private uint ReadUInt32(uint address) => BitConverter.ToUInt32(Read(address, 4));
    private float ReadSingle(uint address) => BitConverter.ToSingle(Read(address, 4));
    private byte ReadByte(uint address) => Read(address, 1)[0];
    private void WriteInt32(uint address, int value) => Write(address, BitConverter.GetBytes(value));

    private byte[] Read(uint address, int count)
    {
        EnsureConnected();
        var data = new byte[count];
        if (!NativeMethods.ReadProcessMemory(_handle, (nint)address, data, (nuint)count, out var read) || read != (nuint)count)
            throw Win32($"Could not read PvZ memory at 0x{address:X8}");
        return data;
    }

    private void Write(uint address, byte[] data)
    {
        EnsureConnected();
        if (!NativeMethods.WriteProcessMemory(_handle, (nint)address, data, (nuint)data.Length, out var written) || written != (nuint)data.Length)
            throw Win32($"Could not write PvZ memory at 0x{address:X8}");
    }

    private bool TryWrite(uint address, byte[] data)
    {
        try { Write(address, data); return true; } catch { return false; }
    }

    private void VerifyBytes(uint address, byte[] expected, string label)
    {
        var actual = Read(address, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"Refused {label}: expected {Convert.ToHexString(expected)} at 0x{address:X8}, found {Convert.ToHexString(actual)}. Another mod or an unexpected build may be active.");
    }

    private void EnsureConnected()
    {
        if (!IsConnected) throw new InvalidOperationException("PvZ is not connected.");
    }

    private static Win32Exception Win32(string message) => new(System.Runtime.InteropServices.Marshal.GetLastWin32Error(), message);
}

internal sealed record GameState(int Sun, int GameUi, int GameMode, int Scene, bool HasBoard);
internal sealed record PlantState(int Row, int Column, int Type);
internal sealed record ZombieState(int Row, int Type, float X, int Health);
internal sealed record SeedPacketState(int Index, int Type, int CooldownPast, int CooldownTotal, bool Ready);

