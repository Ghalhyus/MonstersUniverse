using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using MonsutaPotaru.Services;
using PKHeX.Core;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Combats : noms français, équipes tirées des sauvegardes, protocole de Showdown traduit pour chaque joueur, et vrais
/// combats dans le simulateur (<c>showdown/combat.js</c>, installé par <c>npm ci --prefix showdown</c>).
/// </summary>
public sealed class BattleTests
{
    private static readonly GameStrings French = GameInfo.GetStrings("fr");

    // ---------- Noms ----------

    [Fact]
    public void Names_FromShowdown_InFrench()
    {
        Assert.Equal(French.movelist[237], BattleNames.Move("Hidden Power Dark 70")); // nom affiché par Showdown
        Assert.Equal(French.movelist[85], BattleNames.Move("Thunderbolt"));
        Assert.Equal(French.itemlist[221], BattleNames.Item("King's Rock")); // « King’s Rock » chez PKHeX
        Assert.Equal(French.itemlist[259], BattleNames.Item("Stick")); // « Leek » chez PKHeX
        Assert.Equal(French.itemlist[4], BattleNames.Item("Poke Ball"));
        Assert.Equal(French.abilitylist[22], BattleNames.Ability("Intimidate"));
        Assert.Equal(French.types[12], BattleNames.MoveType("Thunderbolt")); // Électrik
        Assert.Equal(French.types[0], BattleNames.MoveType("Tackle")); // Normal : numéro 0
        Assert.Equal(French.types[16], BattleNames.MoveType("Hidden Power Dark 70")); // Ténèbres

        Assert.Equal((29, "", French.specieslist[29]), BattleNames.SpeciesOf("Nidoran-F, L50, F"));
        Assert.Equal((250, "", French.specieslist[250]), BattleNames.SpeciesOf("Ho-Oh, L70")); // tiret dans le nom
        Assert.Equal((386, "Speed", "Deoxys"), BattleNames.SpeciesOf("Deoxys-Speed, L50"));
        Assert.Equal((351, "Rainy", French.specieslist[351]), BattleNames.SpeciesOf("Castform-Rainy"));
        Assert.Equal((201, "Question", French.specieslist[201]), BattleNames.SpeciesOf("Unown-Question, L20"));
        for (byte form = 0; form < 28; form++) // lettres de Zarbi : A (sans forme), B à Z, « ! », « ? »
            Assert.Equal(form, BattleNames.FormIndex(201, BattleNames.UnownForme(form)));
        Assert.Equal(["", "B", "Z", "Exclamation"], new byte[] { 0, 1, 25, 26 }.Select(BattleNames.UnownForme));
    }

    // ---------- Équipes ----------

    private static SaveFile Save(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Sauvegardes", file);
        Assert.True(SaveUtil.TryGetSaveFile(File.ReadAllBytes(path), out var sav, file));
        return sav!;
    }

    [Fact]
    public void Team_FromALeafGreenSave()
    {
        var sav = Save("vert-feuille.sav");
        var spearow = BattleTeam.ToSet(sav.GetPartySlotAtIndex(0), sav);
        Assert.Equal(("Piafou", 21, 12), (spearow.Name, spearow.Species, spearow.Level)); // surnom, Piafabec
        Assert.Equal([64, 45, 43], spearow.Moves); // Picpic, Rugissement, Groz'Yeux
        Assert.Equal([0, 0, 0], spearow.PpUps);
        Assert.Equal(51, spearow.Ability); // Regard Vif
        Assert.Equal("Roucool", BattleTeam.ToSet(sav.GetPartySlotAtIndex(2), sav).Name); // pas surnommé : pas « ROUCOOL »

        Assert.Equal(9, BattleTeam.Candidates(sav).Count()); // équipe et boîtes, sans œufs
        Assert.Equal("gen3customgame", BattleTeam.Format(3));
        Assert.Null(BattleTeam.Format(4));
        Assert.Equal(["Speed", "Attack", "Defense", ""],
            new[] { GameVersion.E, GameVersion.FR, GameVersion.LG, GameVersion.R }.Select(BattleTeam.DeoxysForme));
    }

    // ---------- Protocole ----------

    [Fact]
    public void Protocol_SecretLinesForTheirOwnerOnly()
    {
        const string chunk = "update\n|t:|123\n|move|p1a: Pika|Surf|p2a: Geo\n|split|p2\n|-damage|p2a: Geo|51/115\n|-damage|p2a: Geo|45/100\n|debug|x";
        var p2 = BattleProtocol.Lines(chunk, "p2");
        Assert.Equal(["move", "-damage"], p2.Select(l => l.Kind));
        Assert.Equal("51/115", p2[1].Arg(1));
        Assert.Equal("45/100", BattleProtocol.Lines(chunk, "p1")[1].Arg(1));

        var line = BattleLine.Parse("|-damage|p1a: Pika|80/110|[from] item: Leftovers|[of] p2a: Geo");
        Assert.Equal(("item: Leftovers", "p2a: Geo"), (line.Tag("from"), line.Tag("of")));
        Assert.Equal(("p2", "Geo"), BattleProtocol.Who("p2a: Geo"));
        Assert.Equal((0, 0, "fnt"), BattleProtocol.Condition("0 fnt"));
    }

    [Fact]
    public void View_SpeaksFrench_FromEachSide()
    {
        var lines = new[]
        {
            "|player|p1|Sacha||", "|player|p2|Ondine||", "|teamsize|p1|2", "|teamsize|p2|1",
            "|switch|p1a: Pika|Pikachu, L50, M, shiny|110/110", "|switch|p2a: Abo|Ekans, L48, F|100/100",
            "|move|p1a: Pika|Thunderbolt|p2a: Abo", "|-supereffective|p2a: Abo", "|-damage|p2a: Abo|20/100",
            "|-unboost|p2a: Abo|atk|1", "|-boost|p1a: Pika|spa|2", "|-status|p2a: Abo|par|[from] ability: Static|[of] p1a: Pika",
            "|-weather|RainDance|[from] ability: Drizzle|[of] p2a: Abo", "|-sidestart|p1: Sacha|Reflect",
            "|-damage|p2a: Abo|0 fnt", "|faint|p2a: Abo", "|win|Sacha",
        }.Select(BattleLine.Parse).ToList();

        var sacha = new BattleView("p1");
        var steps = sacha.Apply(lines);
        Assert.Empty(sacha.Unhandled);
        Assert.Equal(
        [
            "Go ! Pika !", "Ondine envoie Abo !", "Pika utilise Tonnerre !", "C'est super efficace !",
            "L'Attaque d'Abo ennemi baisse !", "L'Attaque Spéciale de Pika augmente beaucoup !",
            "Abo ennemi est paralysé par Statik de Pika !", "Crachin d'Abo ennemi ! Il commence à pleuvoir !",
            "Protection renforce la Défense de ton équipe !", "Abo ennemi est K.O. !", "Tu as battu Ondine !",
        ], steps.Where(s => s.Text is not null).Select(s => s.Text));
        Assert.Equal(BattleOutcome.Won, sacha.Outcome);
        var scene = sacha.Scene;
        Assert.Equal(("Pika", 25, true, 110), (scene.Me!.Name, (int)scene.Me.Species, scene.Me.Shiny, scene.Me.MaxHp));
        Assert.Equal((0, 0, 2, 2, "RainDance"), (scene.Foe!.Hp, scene.FoeLeft, scene.MyLeft, scene.MyTeam, scene.Weather));
        Assert.Contains(steps, s => s.Cue == BattleCue.Attack && s.Mine); // Pika s'avance pour attaquer

        var ondine = new BattleView("p2");
        var seen = ondine.Apply(lines).Where(s => s.Text is not null).Select(s => s.Text).ToList();
        Assert.Equal(["Sacha envoie Pika !", "Go ! Abo !", "Pika ennemi utilise Tonnerre !"], seen.Take(3));
        Assert.Contains("Protection renforce la Défense de l'équipe ennemie !", seen);
        Assert.Equal(("Sacha a gagné le combat !", BattleOutcome.Lost), (seen[^1], ondine.Outcome));
    }

    // ---------- Vrais combats dans le simulateur ----------

    /// <summary>Dossier du simulateur (à la racine du dépôt), installé : sinon le test échoue avec la commande à lancer.</summary>
    internal static ShowdownOptions Showdown([CallerFilePath] string source = "")
    {
        var folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "showdown"));
        Assert.True(Directory.Exists(Path.Combine(folder, "node_modules", "@pkmn", "sim")),
            "Simulateur de combat absent : lancer « npm ci --prefix showdown » à la racine du dépôt.");
        return new ShowdownOptions("node", folder);
    }

    /// <summary>Un combat qui se déroule dans le simulateur, lu au rythme des tests (les deux joueurs à la fois).</summary>
    private sealed class Run : IAsyncDisposable
    {
        private readonly ShowdownBattle battle;
        private readonly BlockingCollection<string> chunks = [];
        public BattleView P1 { get; } = new("p1");
        public BattleView P2 { get; } = new("p2");
        public List<BattleStep> Steps1 { get; } = [];
        public List<BattleStep> Steps2 { get; } = [];
        public bool Over => P1.Outcome != BattleOutcome.None;

        public Run(BattleSetup setup)
        {
            battle = ShowdownBattle.Start(Showdown(), setup, NullLogger.Instance);
            battle.Chunk += chunks.Add;
        }

        /// <summary>Lit le simulateur jusqu'aux requêtes des deux joueurs (ou la fin du combat).</summary>
        public void Pump()
        {
            var requests = 0;
            while (requests < 2 && !Over)
            {
                Assert.True(chunks.TryTake(out var chunk, TimeSpan.FromSeconds(15)), "le simulateur ne répond plus");
                if (BattleProtocol.Request(chunk) is var (side, request))
                {
                    (side == "p1" ? P1 : P2).Request = request;
                    requests++;
                }
                Assert.True(BattleProtocol.Error(chunk) is null, $"choix refusé : {BattleProtocol.Error(chunk)}");
                Steps1.AddRange(P1.Apply(BattleProtocol.Lines(chunk, "p1")));
                Steps2.AddRange(P2.Apply(BattleProtocol.Lines(chunk, "p2")));
            }
        }

        public void Send(string command) => battle.Send(command);

        /// <summary>
        /// Joue jusqu'au bout, comme le site : chaque requête qui attend une action reçoit aussitôt un choix ; un choix
        /// refusé (« Unavailable choice » : capacité bloquée que la requête ne montrait pas) est suivi d'une requête corrigée,
        /// qui reçoit un nouveau choix. Match nul forcé au-delà de 300 tours.
        /// </summary>
        public void PlayRandomly(Random rng)
        {
            var forcedTie = false;
            foreach (var view in new[] { P1, P2 })
            {
                if (view.Request is { } first && Choose(first, rng) is { } choice)
                    Send($"{view.Viewer} {choice}");
            }
            while (!Over)
            {
                Assert.True(chunks.TryTake(out var chunk, TimeSpan.FromSeconds(15)), "le simulateur ne répond plus");
                Steps1.AddRange(P1.Apply(BattleProtocol.Lines(chunk, "p1")));
                Steps2.AddRange(P2.Apply(BattleProtocol.Lines(chunk, "p2")));
                if (BattleProtocol.Request(chunk) is var (side, request))
                {
                    var view = side == "p1" ? P1 : P2;
                    view.Request = request;
                    if (Choose(request, rng) is { } choice)
                        Send($"{side} {choice}");
                }
                if (!forcedTie && P1.Turn > 300)
                {
                    Send("forcetie");
                    forcedTie = true;
                }
            }
        }

        public ValueTask DisposeAsync() => battle.DisposeAsync();
    }

    private static BattlePlayer Player(string name, string file)
    {
        var sav = Save(file);
        return new BattlePlayer(name, Enumerable.Range(0, sav.PartyCount).Select(i => BattleTeam.ToSet(sav.GetPartySlotAtIndex(i), sav)).ToList());
    }

    [Fact]
    public async Task RealSaves_Battle_TranslatedForEachPlayer()
    {
        var p1 = Player("Sacha", "vert-feuille.sav");
        var p2 = Player("Ondine", "rouge-feu.sav");
        await using var run = new Run(new BattleSetup("gen3customgame", [1, 2, 3, 4], p1, p2));
        run.Pump();
        var lead = p2.Team[0].Name;
        Assert.Equal(["Go ! Piafou !", $"Ondine envoie {lead} !"], run.Steps1.Select(s => s.Text).OfType<string>().Take(2));
        Assert.Contains("Sacha envoie Piafou !", run.Steps2.Select(s => s.Text));

        // Picpic (capacité 1) : les PP réels (35, sans PP Plus), et le message des deux côtés.
        var peck = run.P1.Request!.Active![0].Moves[0];
        Assert.Equal(("Peck", 35), (peck.Move, peck.MaxPp));
        run.Send("p1 move 1");
        run.Send("p2 move 1");
        run.Pump();
        Assert.Contains("Piafou utilise Picpic !", run.Steps1.Select(s => s.Text));
        Assert.Contains("Piafou ennemi utilise Picpic !", run.Steps2.Select(s => s.Text));
        Assert.Equal(34, run.P1.Request!.Active![0].Moves[0].Pp);

        // Abandon (ou temps écoulé) : forcé par le site.
        run.Send("forcelose p2");
        run.Pump();
        Assert.Equal((BattleOutcome.Won, BattleOutcome.Lost), (run.P1.Outcome, run.P2.Outcome));
        Assert.Empty(run.P1.Unhandled.Concat(run.P2.Unhandled));
    }

    /// <summary>
    /// Zarbi garde sa lettre (forme d'aspect de Showdown, pour l'image) ; chaque joueur voit les sprites de son jeu : de face
    /// pour l'adversaire, de dos pour les siens, Deoxys dans la forme de son jeu.
    /// </summary>
    [Fact]
    public async Task Unown_KeepsItsLetter_SeenWithTheSpritesOfTheViewersGame()
    {
        var leafGreen = Save("vert-feuille.sav");
        var unown = BattleTeam.ToSet(leafGreen.GetPartySlotAtIndex(0), leafGreen) with
        {
            Name = "Zarbi", Species = 201, Forme = BattleNames.UnownForme(27), Item = 0, Ability = 26, Moves = [237], PpUps = [0],
        };
        await using var run = new Run(new BattleSetup("gen3customgame", [1, 2, 3, 4], new BattlePlayer("Sacha", [unown]), Player("Ondine", "rouge-feu.sav")));
        run.Pump();
        var foe = run.P2.Scene.Foe!;
        Assert.Equal((201, "Question"), (foe.Species, foe.Forme));
        Assert.Empty(run.P1.Unhandled.Concat(run.P2.Unhandled));

        var sprites = BattleSprites.For(leafGreen); // Rouge Feu / Vert Feuille : PKHeX répond Rouge Feu
        const string folder = "images/livingdex/rouge-feu-vert-feuille/";
        Assert.Equal($"{folder}201-question.png", sprites.Url(foe, back: false));
        Assert.Equal($"{folder}dos/chromatique/201-question.png", sprites.Url(foe with { Shiny = true }, back: true));
        Assert.Equal($"{folder}dos/201-b.png", sprites.Url(foe with { Forme = "B" }, back: true));
        Assert.Equal($"{folder}386-attack.png", sprites.Url(foe with { Species = 386, Forme = "Speed" }, back: false)); // Deoxys d'Émeraude
        Assert.Equal($"{folder}chromatique/351-sunny.png", sprites.Url(foe with { Species = 351, Forme = "Sunny", Shiny = true }, back: false));
        Assert.Equal($"{folder}dos/6.png", sprites.Url(foe with { Species = 6, Forme = "" }, back: true));
        Assert.Equal(BattleSprites.Fallback(foe), BattleSprites.None.Url(foe, back: true));
        Assert.StartsWith("images/pokemon/", BattleSprites.Fallback(foe));
        Assert.Equal(BattleSprites.None, BattleSprites.For(Save("ultra-soleil.sav"))); // 7e génération : pas de sprites du jeu
    }

    /// <summary>
    /// 25 combats entre équipes tirées au hasard dans toute la 3e génération (espèces, capacités, talents, objets), choix au
    /// hasard : chacun va à son terme, et la vue sait traduire chaque ligne pour les deux joueurs.
    /// </summary>
    [Fact]
    public async Task RandomBattles_EndAndAreFullyTranslated()
    {
        var unhandled = new SortedSet<string>(StringComparer.Ordinal);
        for (var seed = 1; seed <= 25; seed++)
        {
            var rng = new Random(seed);
            await using var run = new Run(new BattleSetup("gen3customgame", [seed, 7, 11, 13], RandomPlayer("A", rng), RandomPlayer("B", rng)));
            run.Pump();
            run.PlayRandomly(rng);
            Assert.NotEqual(BattleOutcome.None, run.P2.Outcome);
            unhandled.UnionWith(run.P1.Unhandled.Concat(run.P2.Unhandled));
        }
        Assert.True(unhandled.Count == 0, "lignes non traduites :\n" + string.Join("\n", unhandled));
    }

    private static readonly string[] DeoxysFormes = ["", "Attack", "Defense", "Speed"];

    private static readonly int[] HeldItems = [.. Enumerable.Range(149, 27), .. Enumerable.Range(213, 47)]; // baies et objets tenus de la 3e génération

    private static BattlePlayer RandomPlayer(string name, Random rng)
    {
        var natures = GameInfo.GetStrings("en").natures;
        var team = Enumerable.Range(0, 6).Select(_ =>
        {
            var species = (ushort)rng.Next(1, 387);
            var personal = PersonalTable.E[species];
            var moves = Enumerable.Range(0, 4).Select(_ => rng.Next(1, 355)).Distinct().ToList();
            BattleStats Ivs() => new(rng.Next(32), rng.Next(32), rng.Next(32), rng.Next(32), rng.Next(32), rng.Next(32));
            return new BattleSet(French.specieslist[species], species, species == 386 ? DeoxysFormes[rng.Next(4)] : "",
                rng.Next(3) == 0 ? 0 : HeldItems[rng.Next(HeldItems.Length)], rng.Next(2) == 0 ? personal.Ability1 : personal.Ability2,
                moves, moves.Select(_ => rng.Next(4)).ToList(), natures[rng.Next(25)], "", rng.Next(20) == 0,
                rng.Next(20, 101), rng.Next(256), Ivs(), new BattleStats(0, 0, 0, 0, 0, 0));
        }).ToList();
        return new BattlePlayer(name, team);
    }

    /// <summary>Choix au hasard parmi ceux que la requête permet (null : ce joueur attend).</summary>
    private static string? Choose(BattleRequest request, Random rng)
    {
        if (!request.MustAct)
            return null;
        var bench = request.Side.Pokemon.Select((p, i) => (p, i)).Where(x => !x.p.Active && !x.p.Condition.EndsWith("fnt")).Select(x => x.i + 1).ToList();
        if (request.MustSwitch)
            return bench.Count > 0 ? $"switch {bench[rng.Next(bench.Count)]}" : null;
        var active = request.Active![0];
        if (bench.Count > 0 && !active.Trapped && !active.MaybeTrapped && rng.Next(8) == 0)
            return $"switch {bench[rng.Next(bench.Count)]}";
        var moves = active.Moves.Select((m, i) => (m, i)).Where(x => !x.m.IsDisabled).Select(x => x.i + 1).ToList();
        return $"move {(moves.Count > 0 ? moves[rng.Next(moves.Count)] : 1)}";
    }
}
