namespace PvZController;

// Event-based streamer lines. {tokens} are filled from what is really happening; a line is
// skipped when it needs a value we do not have. Openers and closers are mixed in at random
// so the same core line almost never sounds the same twice.
internal sealed class PersonaLines
{
    private readonly Queue<string> _recent = new();
    private readonly Queue<string> _recentCores = new();

    private static readonly string[] Openers =
    [
        "Alright chat,", "Okay listen,", "Real talk,", "Not gonna lie,", "Be honest,", "Hey,",
        "Look,", "So,", "Bro,", "Chat,", "Quick update,", "Fun fact,", "Just so you know,",
        "Ha,", "Okay okay,", "Wait,", "Imagine this,", "For the record,",
    ];

    private static readonly string[] Closers =
    [
        "Easy.", "Watch this.", "Try me.", "Don't blink.", "Just saying.", "You're welcome.",
        "That's the difference.", "Sit down.", "Next.", "Keep watching.", "Not even close.",
        "Say it with me, easy.", "I'm too good at this.", "Write that down.", "Free content.",
        "Cry about it.", "Okay, moving on.", "Classic me.", "No big deal.", "Too smooth.",
    ];

    // Categories that should sound conversational get openers/closers mixed in.
    private static readonly HashSet<string> Mixable = ["bait", "challenge", "welcome", "calm", "score", "other", "horde"];

    private static readonly Dictionary<string, string[]> Lines = new()
    {
        ["connect"] =
        [
            "Alright chat, I'm live. Watch me do this the easy way.",
            "I'm online. Chat, try to beat me. You won't.",
            "Stream's on. Let's see who thinks they can take me down.",
            "I'm here, I'm warmed up, and I'm ready to embarrass some zombies.",
            "Welcome to the show. The bot is awake.",
            "Lights on, mic on, zombies are in trouble.",
        ],
        ["start"] =
        [
            "New run, chat. Let's see how long I make it look easy.",
            "Run number {run}. Let's go, chat.",
            "Fresh lawn, fresh zombies. Same result, I win.",
            "New game. Your best bet is to send big zombies and hope.",
            "Let's start. Timer's running, chat.",
            "Round {run} begins. Place your bets, chat.",
            "Clock is running. Let's see if chat can break me this time.",
            "New lawn. Same me. Let's get it.",
            "Here we go again. Try to keep up.",
        ],
        ["loss"] =
        [
            "Okay, okay, that's loss number {losses}. Chat got lucky.",
            "Loss number {losses}. That one was on the lag, not me.",
            "No no no, wait, that doesn't count! Chat, that was a glitch!",
            "Okay, that one actually hurt. Chat, enjoy it while it lasts.",
            "They broke through. Fine. Ten out of ten, chat, well played.",
            "I'm so mad right now. Run it back!",
            "That's a loss. Don't screenshot it. Okay, you can screenshot it.",
            "I lasted {time}. Beat that, chat. Oh wait, you can't.",
            "{losses} losses. Chat feels strong today. It won't last.",
            "I went down at {time}. Best is {best}. Come get it.",
            "Fine. I lost. Run it back, I'll do better.",
            "Chat won that one, and you all act like it's a big deal.",
            "That's {losses} losses. I'm still the main character here.",
            "They got me at {time}. Enjoy it, because it won't happen again.",
            "Okay, that hurt. Round {run} is going to be different.",
            "You got me. Screenshot it, it won't happen twice.",
            "{time} and out. Not my best work, I admit it.",
            "Loss {losses}. Chat, you are way too happy right now.",
        ],
        ["lossstats"] =
        [
            "That's {losses} losses now. Chat is feeling good, I can tell.",
            "I lasted {time} that run. Best is still {best}.",
            "Final time {time}. Write it down, you won't see it again.",
            "{losses} losses so far. Every one of them was somebody's gift.",
            "Run over at {time}. I'm already planning the next one.",
        ],
        ["lossbait"] =
        [
            "Type GG in chat if you got me. Now try it again.",
            "That was your best shot, chat. Do it again, I dare you.",
            "Type L in chat if you think I lose again. Go.",
            "Send more zombies, chat. I'm not done.",
            "Next run starts now. Who's sending the first Gargantuar?",
            "Beat {best}? Not a chance. Run it back.",
        ],
        ["chatjoin"] =
        [
            "Welcome in, {name}. Sit down and watch the bot cook.",
            "Hey {name}, welcome. Try to make me lose.",
            "{name} just walked in. Good timing, I'm about to show off.",
            "Look who joined, it's {name}. Welcome to the lawn.",
            "Welcome, {name}. I've lost {losses} times so far, don't tell anyone.",
            "{name} is here. Everybody act natural.",
            "Hi {name}. Beat {best} and I'll be impressed.",
        ],
        ["chatfollow"] =
        [
            "{name} just followed! Respect. You picked the right stream.",
            "Thank you for the follow, {name}. You won't regret it.",
            "A new follower, {name}! Chat, show some love.",
            "{name} followed me. Smart move, honestly.",
        ],
        ["chatgift"] =
        [
            "{name} sent a {gift}! Okay, I see you, big spender.",
            "Thank you {name} for the {gift}. Now watch me use it.",
            "{name} dropped a {gift}. That's real love, chat.",
            "A {gift} from {name}! You're my favorite right now.",
            "{name}, thanks for the {gift}. I'll make it worth it.",
        ],
        ["chatcomment"] =
        [
            "{name} says: {text}. Cute. Watch me.",
            "{name} said {text}. I heard you, I'm still winning.",
            "Okay {name}, {text}? Noted.",
            "{name} wrote {text}. Chat, did you see that?",
            "Shout out to {name} for saying {text}.",
        ],
        ["chatgeneric"] =
        [
            "{name} is talking. I love the attention.",
            "Okay {name}, I hear you. Keep it coming.",
            "{name} has something to say, and I'm too busy winning to answer.",
            "Thanks for the comment, {name}. Now watch this.",
            "{name} is in the chat. Everybody be nice.",
            "{name}, you want to beat me? Send a big zombie.",
        ],
        ["chatgreet"] =
        [
            "Hey {name}! Welcome. Make yourself at home.",
            "Hello {name}. Nice to see you in the chat.",
            "What's up, {name}. Ready to watch me win?",
        ],
        ["chattaunt"] =
        [
            "{name} thinks I'm bad? Watch the bot, {name}. Watch.",
            "Oh {name}, you think you can do better? Prove it.",
            "{name} is trying to get under my skin. It's not working.",
            "Bold words, {name}. Send a Gargantuar and let's see.",
            "{name}, say that again when you beat my best time.",
        ],
        ["chatthanks"] =
        [
            "Thanks {name}! That's the energy I want.",
            "{name} gets it. The bot is the best.",
            "Appreciate you, {name}. Stay for the show.",
        ],
        ["newbest"] =
        [
            "New best time, {time}! Chat, you're cooked.",
            "{time}! That's my best ever. Beat it, I dare you.",
            "A new record, {time}. Somebody tell chat to try harder.",
            "Best run ever at {time}, and I still lost? Rude, but I'll take it.",
        ],
        ["breach"] =
        [
            "Row {row} is leaking, relax, I've got it.",
            "Zombie at the door in row {row}. Totally planned.",
            "Row {row}? I let them in to make it fun.",
            "Okay, row {row} got scary for a second.",
            "Row {row} is in trouble, watch me fix it.",
            "They're at the house in row {row}! Calm down, chat.",
            "Row {row} almost fell. Nobody saw that, right?",
            "Okay row {row}, you're making me work for it.",
            "That one's close in row {row}. Heart rate going up.",
            "Row {row} needs help right now. I'm on it.",
            "Don't panic, row {row} is fine. Probably.",
        ],
        ["plantlost"] =
        [
            "They ate my {plant} in row {row}. Rude.",
            "Lost a {plant} in row {row}. Whatever, I've got more.",
            "RIP {plant}. You were a good one.",
            "Row {row} just lost a {plant}. Chat, I see you.",
            "Fine, take the {plant}. I'm still winning.",
            "The {plant} in row {row} is gone. I'll plant a better one.",
            "A zombie just ate my {plant}. Disrespectful.",
            "{plant} down in row {row}. Moving on, nothing to see.",
            "Goodbye, {plant}. You did your best.",
            "That {plant} cost me nothing. Sun is free for me.",
        ],
        ["bigzombie"] =
        [
            "{zombie} in row {row}? That's your big plan?",
            "A {zombie} shows up. Cute. Watch this.",
            "Oh, a {zombie}. Chat, you can do better than that.",
            "{zombie} coming down row {row}. Easy work.",
            "Is that a {zombie} in row {row}? I've seen scarier.",
            "Here comes a {zombie}. Let me find a plant for you, buddy.",
            "A {zombie} walks in. Row {row}, you're on your own for a second.",
            "{zombie} spotted in row {row}. Interesting choice.",
            "Chat sends a {zombie}. I send it right back.",
            "Okay, that {zombie} actually looks scary. Just kidding.",
        ],
        ["horde"] =
        [
            "{count} zombies on screen. Look at me not panic.",
            "That's {count} zombies. Chat, you really want me to lose, huh.",
            "{count} zombies and I'm still smiling.",
            "Wow, {count} zombies. Did somebody send gifts?",
            "{count} zombies, and zero fear.",
            "There are {count} zombies out there. Lovely crowd.",
            "Count them, {count} zombies. And I'm still here.",
        ],
        ["calm"] =
        [
            "Nothing to do. This lawn is too easy.",
            "Zombies are slow today. Come on, chat, send something real.",
            "I'm so bored. Send a Gargantuar or something.",
            "This is a walk in the park, honestly.",
            "I could play this half asleep. Watch.",
            "Is anybody even trying to beat me?",
            "Quiet lawn. Suspicious. Send some zombies, chat.",
            "I'm just standing here looking perfect.",
            "My plants are doing all the work. I'm just here for the vibes.",
            "The zombies took a break. Typical.",
            "Is this the hardest it gets? Come on.",
            "I'm so good at this it's actually boring.",
        ],
        ["bait"] =
        [
            "Type one in chat if you think I lose before five minutes.",
            "Chat, type a skull if you think I'm going down.",
            "Who here thinks the bot loses next? Type it.",
            "Type yes if you think you could do better than me.",
            "Bet you can't type faster than I plant. Try it.",
            "Chat, say something. It's too quiet. Is everyone scared?",
            "Say my name in chat if you want me to lose. I'll wait.",
            "Drop a heart if you want me to survive. Drop a skull if you don't.",
            "How long do you think I last this time? Put it in chat.",
            "Chat, vote now. Does the bot survive ten minutes? Yes or no.",
            "I bet nobody in here can send a zombie that scares me.",
            "Type the biggest zombie you know. I'll show you it's nothing.",
            "Comment if you've never seen a bot this smooth.",
            "Who's the best player in chat? Prove it. Beat my time.",
            "If you think I'm trash, say it in chat. I can take it.",
            "Hit the like button if you want to see me sweat.",
            "Tap the screen if you believe in the zombies.",
            "Chat, pick a row. I'll defend it better than you can.",
        ],
        ["welcome"] =
        [
            "Welcome in, new people. Sit down, watch the bot cook.",
            "Hey, welcome to the stream. Don't forget to follow.",
            "New people, welcome. I've lost {losses} times, but who's counting.",
            "Welcome everybody. Try to beat my best time of {best}.",
            "If you just got here, welcome. Follow so you see me lose.",
            "Hello new faces. The goal is simple, make me lose.",
            "Welcome to the lawn. The rules are simple. Beat the bot.",
            "New here? Good. Sit back and enjoy the show.",
            "Whoever just joined, welcome. You picked a good time.",
            "Thanks for stopping by. Follow if you like winning.",
            "Hey new viewers. I'm the bot. I never get tired.",
            "Welcome, welcome. Grab a seat and watch zombies suffer.",
            "Glad you're here. Now help me by sending zombies.",
        ],
        ["challenge"] =
        [
            "Send a Gargantuar, I dare you.",
            "Drop a gift and I'll send you a zombie. Kidding. Watch me.",
            "Can anybody in chat actually beat me? Doubt it.",
            "Chat, I'm giving you a free shot. Send everything.",
            "Prove you can beat this bot. Go ahead.",
            "I've survived {time} this run. Can you do that?",
            "Beat {best} and I'll shut up. You can't though.",
            "I'll make you a deal. Send a giant zombie and see what happens.",
            "You can't hurt me. Go ahead, try.",
            "Make it harder. I'm begging you.",
            "Nobody has beaten {best} yet. Will it be you?",
            "Gifts make zombies. Zombies make me look good.",
            "I dare chat to send ten zombies at once.",
            "If you beat me, I'll admit you're good. That's never going to happen.",
        ],
        ["score"] =
        [
            "This run is at {time}. Best is {best}.",
            "Losses so far: {losses}. Chat is trying hard.",
            "Run number {run}. Best time is {best}. Beat it.",
            "{losses} losses today. And I still look this good.",
            "Current time is {time}. The record is {best}.",
            "I'm at {time} right now. Can I break {best}?",
            "That's {losses} losses so far. Chat's winning, apparently.",
            "{time} on the clock. Still going.",
        ],
        ["PutZombie"] =
        [
            "Oh, you sent a {zombie}. Cute.",
            "A {zombie}, really? That's all you've got?",
            "Thanks for the {zombie}. I'll be sending it back.",
            "You sent a {zombie}. I felt that. Not really.",
            "Is a {zombie} supposed to scare me?",
            "More zombies, chat? I can do this all day.",
            "A {zombie} just spawned. Somebody's feeling brave.",
            "Look at that, a {zombie}. You're really going for it.",
            "Nice try with the {zombie}. Better luck next time.",
            "A {zombie} for me? You shouldn't have.",
            "Chat spent real money on a {zombie}. That's sad.",
            "I see you, sending a {zombie}. Respect, but no.",
        ],
        ["ClearAllPlants"] =
        [
            "You wiped my whole lawn?! Okay, that was kind of rude.",
            "My plants! Chat, you monster. I'll rebuild.",
            "Everything's gone. Fine, I'll start again, watch me.",
            "You cleared the lawn? Okay, now it's personal.",
            "Where did my plants go? Chat, give them back.",
        ],
        ["KillAllZombies"] =
        [
            "You killed all the zombies? Boring. Send them back.",
            "Free clear, thanks chat. I'll take it.",
            "No more zombies? That's not very sporting.",
            "The lawn is empty. Somebody send more, please.",
        ],
        ["SetSun"] =
        [
            "Free sun? Thanks, but I never needed it.",
            "More sun. I'm unstoppable now.",
            "Extra sun, sure. It doesn't change anything.",
        ],
        ["other"] =
        [
            "Thanks for the support, chat. Now watch me.",
            "Love that. Keep it coming, chat.",
            "Somebody in chat is feeling generous. Respect.",
            "You love to see it. Thanks, chat.",
            "That's what I like to see. Keep going.",
            "Big thanks to whoever did that. You're my favorite right now.",
            "Chat is showing love. I feel it.",
            "That's the energy I want. More of it, please.",
            "Appreciate you, chat. Really.",
        ],
    };

    internal string? Next(string category, IReadOnlyDictionary<string, string>? vars = null)
    {
        if (!Lines.TryGetValue(category, out var choices)) choices = Lines["other"];
        var usable = choices.Where(l => Fillable(l, vars)).ToArray();
        if (usable.Length == 0) return null;
        var fresh = usable.Where(l => !_recentCores.Contains(l)).ToArray();
        if (fresh.Length == 0) fresh = usable;
        var core = fresh[Random.Shared.Next(fresh.Length)];
        _recentCores.Enqueue(core);
        while (_recentCores.Count > Math.Max(1, Math.Min(usable.Length - 1, 30))) _recentCores.Dequeue();

        var result = core;
        if (vars is not null)
            foreach (var (key, value) in vars) result = result.Replace("{" + key + "}", value);

        if (Mixable.Contains(category))
        {
            // Randomly dress the line up so repeats of the same core still sound different.
            var roll = Random.Shared.Next(10);
            if (roll < 3) result = Pick(Openers) + " " + LowerFirst(result);
            else if (roll < 6) result = result + " " + Pick(Closers);
            else if (roll < 7) result = Pick(Openers) + " " + LowerFirst(result) + " " + Pick(Closers);
        }
        // Never say the exact same final sentence twice in a short window.
        if (_recent.Contains(result)) result = result + " " + Pick(Closers);
        _recent.Enqueue(result);
        while (_recent.Count > 60) _recent.Dequeue();
        return result;
    }

    private static string Pick(string[] pool) => pool[Random.Shared.Next(pool.Length)];

    private static string LowerFirst(string s) =>
        s.Length > 1 && char.IsUpper(s[0]) && !char.IsUpper(s[1]) && s[1] != ' ' && s[1] != '\'' ? char.ToLowerInvariant(s[0]) + s[1..] : s;

    private static bool Fillable(string line, IReadOnlyDictionary<string, string>? vars)
    {
        var i = line.IndexOf('{');
        while (i >= 0)
        {
            var j = line.IndexOf('}', i);
            if (j < 0) break;
            var key = line.Substring(i + 1, j - i - 1);
            if (vars is null || !vars.ContainsKey(key)) return false;
            i = line.IndexOf('{', j);
        }
        return true;
    }
}
