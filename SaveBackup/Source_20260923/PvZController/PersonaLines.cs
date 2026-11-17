namespace PvZController;

internal sealed class PersonaLines
{
    private readonly Queue<string> _recent = new();

    private static readonly Dictionary<string, string[]> Lines = new()
    {
        ["connect"] =
        [
            "The lawn is mine. Let them try.",
            "I am online. The zombies should have stayed home.",
            "Defense is awake. This should be entertaining.",
            "I have the lawn. They have poor timing.",
            "All rows accounted for. Let us see what they brought."
        ],
        ["start"] =
        [
            "Cards picked. Zombies, your turn to be disappointed.",
            "New wave, same lawn, better plan.",
            "The lineup is ready. Try to keep up.",
            "I already know where this is going.",
            "Round started. I like my chances."
        ],
        ["loss"] =
        [
            "A lucky break for the zombies. I am running that back.",
            "That was one round. I learn quickly.",
            "Enjoy that win, zombies. It is not happening twice.",
            "Resetting. The next defense will be cleaner.",
            "Fine. Rematch. I have adjustments to make."
        ],
        ["breach"] =
        [
            "That is a serious push. Good thing I take this lawn personally.",
            "They found a gap. I found the answer.",
            "Close call? I call that a timing exercise.",
            "Not one step closer. I am rebuilding this row.",
            "Pressure on the lawn. Perfect time to show the backup plan.",
            "That crowd is getting bold. Let me fix that.",
            "They are pushing hard. So am I."
        ],
        ["horde"] =
        [
            "A whole crowd? Wonderful. More targets, same result.",
            "That is a lot of zombies for a very small chance.",
            "Big wave. Bigger defense.",
            "They brought numbers. I brought a plan.",
            "The lawn looks busy. I look prepared.",
            "Keep coming. I have room for the whole parade.",
            "This is where the defense gets interesting."
        ],
        ["ordinary"] =
        [
            "Come closer, zombies. I have a plan for every row.",
            "One step at a time. Mine are better placed.",
            "That lane looks thin. Already handling it.",
            "A quiet lawn never lasts. I stay ready.",
            "They keep trying the same approach. I keep improving mine.",
            "Every row has a job. I make sure it gets done.",
            "I see the approach. The plants see the target."
        ],
        ["PutZombie"] =
        [
            "Chat sent reinforcements? I can handle those too.",
            "Another zombie from chat. Very confident of you.",
            "Chat picked a challenger. I picked its landing spot.",
            "More zombies? Good. The defense needed a warmup.",
            "You can send a crowd. I still own the lawn.",
            "Another guest arrives. The plants are ready.",
            "That one looks determined. So am I.",
            "Chat is making this interesting. I approve.",
            "Fresh zombie incoming. Let me make room in the strategy.",
            "I saw that spawn. No surprises here."
        ],
        ["PutPlant"] =
        [
            "A plant from chat. Now we are talking strategy.",
            "A little help from chat. I can work with that.",
            "Nice placement from chat. I will build around it.",
            "Another plant joins the team. Good decision.",
            "Chat is helping? I knew you had taste."
        ],
        ["ClearAllPlants"] =
        [
            "Starting fresh? Bold move. I still have this.",
            "You cleared my board. Watch how fast I rebuild.",
            "Clean slate. The zombies still have a problem.",
            "A full reset? Fine. I like a challenge.",
            "That took my plants. It did not take the plan."
        ],
        ["KillAllZombies"] =
        [
            "That was efficient. Even I am impressed.",
            "And the lawn goes quiet. Excellent timing.",
            "A clean sweep. I almost feel bad for them.",
            "That is one way to end an argument.",
            "All clear. I will be ready for the next wave."
        ],
        ["SetSun"] =
        [
            "Resources secured. Time to get loud.",
            "Sun is covered. Defense gets the good stuff.",
            "Budget approved. The zombies will notice.",
            "Plenty of sun. Plenty of options."
        ],
        ["other"] =
        [
            "Chat made a move. I already have a response.",
            "I saw that. Let us see what happens next.",
            "Interesting choice from chat. I can adapt.",
            "The stream changed the board. I changed the plan."
        ]
    };

    internal string Next(string category)
    {
        if (!Lines.TryGetValue(category, out var choices)) choices = Lines["other"];
        var available = choices.Where(line => !_recent.Contains(line)).ToArray();
        if (available.Length == 0) available = choices;
        var result = available[Random.Shared.Next(available.Length)];
        _recent.Enqueue(result);
        while (_recent.Count > 5) _recent.Dequeue();
        return result;
    }
}
