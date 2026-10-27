namespace MonsutaPotaru.Services;

/// <summary>Ce que l'interface anime pour une étape : le Pokémon qui attaque s'avance, la barre de PV bouge...</summary>
public enum BattleCue { None, Attack, Damage, Heal, Faint, SwitchIn, SwitchOut }

/// <summary>Issue d'un combat, vue par un joueur.</summary>
public enum BattleOutcome { None, Won, Lost, Draw }

/// <summary>
/// Pokémon sur le terrain : nom (surnom), espèce (numéro, forme de Showdown pour Morphéo ou Deoxys, nom français), niveau,
/// sexe (« M », « F », « »), chromatique, PV (exacts pour le sien ; sur 100 pour l'adversaire), statut (« brn », « par »...).
/// </summary>
public sealed record BattleFighter(
    string Name, ushort Species, string Forme, string SpeciesName, int Level, string Gender, bool Shiny,
    int Hp, int MaxHp, string? Status)
{
    /// <summary>PV restants en pourcentage (barre de PV), au moins 1 % tant qu'il n'est pas K.O.</summary>
    public int Percent => MaxHp <= 0 || Hp <= 0 ? 0 : Math.Max(1, (int)Math.Round(Hp * 100.0 / MaxHp));
}

/// <summary>Le terrain vu par un joueur : son Pokémon, celui de l'adversaire, Pokémon restants de chaque côté, météo.</summary>
public sealed record BattleScene(BattleFighter? Me, BattleFighter? Foe, int MyLeft, int MyTeam, int FoeLeft, int FoeTeam, string? Weather);

/// <summary>
/// Une étape à montrer : un message (null : seulement une animation), ce qu'il faut animer et de quel côté
/// (<paramref name="Mine"/> : le Pokémon du joueur), et le terrain après l'étape.
/// </summary>
public sealed record BattleStep(string? Text, BattleCue Cue, bool Mine, BattleScene Scene);

/// <summary>
/// Un combat vu par un joueur (« p1 » ou « p2 ») : applique les lignes du protocole de Showdown, tient l'état du terrain et
/// rend chaque événement en étapes à montrer, en français, comme dans Pokémon Platine (« Pika utilise Surf ! », « Geo
/// ennemi est K.O. ! »). Les lignes qu'elle ne sait pas traduire sont notées dans <see cref="Unhandled"/> (les tests
/// vérifient qu'il n'y en a pas).
/// </summary>
public sealed class BattleView(string viewer)
{
    public string Viewer { get; } = viewer;
    private string Opponent => Viewer == "p1" ? "p2" : "p1";

    private readonly Dictionary<string, string> trainers = [];
    private readonly Dictionary<string, int> teamSize = [];
    private readonly Dictionary<string, int> fainted = [];
    private readonly Dictionary<string, BattleFighter?> active = [];
    private string? weather;

    public BattleOutcome Outcome { get; private set; }

    public int Turn { get; private set; }

    /// <summary>Dernière requête de Showdown pour ce joueur : ses Pokémon (menu « Pokémon ») et ses capacités.</summary>
    public BattleRequest? Request { get; set; }

    /// <summary>Lignes que la vue ne sait pas (encore) traduire, pour les tests.</summary>
    public List<string> Unhandled { get; } = [];

    public string TrainerName(bool mine) => trainers.GetValueOrDefault(mine ? Viewer : Opponent, mine ? "Toi" : "L'adversaire");

    public BattleScene Scene => new(
        active.GetValueOrDefault(Viewer), active.GetValueOrDefault(Opponent),
        Left(Viewer), teamSize.GetValueOrDefault(Viewer), Left(Opponent), teamSize.GetValueOrDefault(Opponent), weather);

    private int Left(string side) => teamSize.GetValueOrDefault(side) - fainted.GetValueOrDefault(side);

    /// <summary>Applique les lignes d'un morceau « update » (<see cref="BattleProtocol.Lines"/>) et rend les étapes à montrer.</summary>
    public List<BattleStep> Apply(IEnumerable<BattleLine> lines)
    {
        var steps = new List<BattleStep>();
        var painSplitShown = false;
        foreach (var line in lines)
        {
            var (text, cue, side) = Translate(line, ref painSplitShown);
            if (text is not null || cue != BattleCue.None)
                steps.Add(new BattleStep(text, cue, side == Viewer, Scene));
        }
        return steps;
    }

    // ---------- Noms vus par le joueur ----------

    /// <summary>« Pika » pour son Pokémon, « Geo ennemi » pour celui de l'adversaire.</summary>
    private string Mon(string ident)
    {
        var (side, name) = BattleProtocol.Who(ident);
        return side == Viewer ? name : $"{name} ennemi";
    }

    private static string? SideOf(string ident) => string.IsNullOrEmpty(ident) ? null : BattleProtocol.Who(ident).Side;

    /// <summary>« ton équipe » ou « l'équipe ennemie » (« p2: Ondine » → camp p2).</summary>
    private string Team(string sideArg) => sideArg.StartsWith(Viewer, StringComparison.Ordinal) ? "ton équipe" : "l'équipe ennemie";

    /// <summary>« de Pika », « d'Abo » : élision devant une voyelle.</summary>
    private static string De(string name) =>
        name.Length > 0 && "AEIOUYÉÈÊÂÎÔÛaeiouyéèêâîôû".Contains(name[0]) ? $"d'{name}" : $"de {name}";

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>« item: Leftovers » → (« item », « Leftovers ») ; « brn » → (« », « brn »).</summary>
    private static (string Kind, string Name) Effect(string? effect)
    {
        if (string.IsNullOrEmpty(effect))
            return ("", "");
        var colon = effect.IndexOf(':');
        return colon < 0 ? ("", effect) : (effect[..colon].Trim(), effect[(colon + 1)..].Trim());
    }

    /// <summary>Nom français d'un effet (capacité, objet, talent), quel que soit son genre.</summary>
    private static string EffectName(string? effect)
    {
        var (kind, name) = Effect(effect);
        return kind switch
        {
            "item" => BattleNames.Item(name),
            "ability" => BattleNames.Ability(name),
            _ => BattleNames.Move(name),
        };
    }

    private static readonly Dictionary<string, (string Name, string Article)> Stats = new()
    {
        ["atk"] = ("Attaque", "L'"), ["def"] = ("Défense", "La "), ["spa"] = ("Attaque Spéciale", "L'"),
        ["spd"] = ("Défense Spéciale", "La "), ["spe"] = ("Vitesse", "La "), ["accuracy"] = ("Précision", "La "),
        ["evasion"] = ("Esquive", "L'"),
    };

    /// <summary>« paralysé », « brûlé »... (accordé au masculin, comme en jeu).</summary>
    private static string StatusAdjective(string status) => status switch
    {
        "brn" => "brûlé", "par" => "paralysé", "slp" => "endormi", "frz" => "gelé", "psn" => "empoisonné",
        "tox" => "gravement empoisonné", _ => status,
    };

    // ---------- État du terrain ----------

    private BattleFighter? Fighter(string ident)
    {
        var (side, name) = BattleProtocol.Who(ident);
        return active.GetValueOrDefault(side) is { } f && f.Name == name ? f : null;
    }

    private void Update(string ident, Func<BattleFighter, BattleFighter> change)
    {
        var side = BattleProtocol.Who(ident).Side;
        if (Fighter(ident) is { } f)
            active[side] = change(f);
    }

    private void SetHp(string ident, string condition)
    {
        var (hp, max, status) = BattleProtocol.Condition(condition);
        Update(ident, f => f with
        {
            Hp = hp,
            MaxHp = max > 0 ? max : f.MaxHp,
            Status = status == "fnt" ? f.Status : status,
        });
    }

    /// <summary>« Pikachu, L50, M, shiny » + « 110/110 » → Pokémon qui entre sur le terrain.</summary>
    private void SwitchIn(string ident, string details, string condition)
    {
        var (side, name) = BattleProtocol.Who(ident);
        var (species, forme, speciesName) = BattleNames.SpeciesOf(details);
        var parts = details.Split(',', StringSplitOptions.TrimEntries);
        var level = parts.Select(p => p.StartsWith('L') && int.TryParse(p[1..], out var l) ? l : 0).FirstOrDefault(l => l > 0, 100);
        var gender = parts.Contains("M") ? "M" : parts.Contains("F") ? "F" : "";
        var (hp, max, status) = BattleProtocol.Condition(condition);
        active[side] = new BattleFighter(name, species, forme, speciesName, level, gender, parts.Contains("shiny"), hp, max, status);
    }

    // ---------- Traduction ----------

    private (string? Text, BattleCue Cue, string? Side) Translate(BattleLine line, ref bool painSplitShown)
    {
        var who = line.Arg(0);
        var side = SideOf(who);
        var from = line.Tag("from");
        var of = line.Tag("of");
        var silent = line.Has("silent");
        switch (line.Kind)
        {
            case "player":
                if (line.Arg(1).Length > 0)
                    trainers[line.Arg(0)] = line.Arg(1);
                return (null, BattleCue.None, null);
            case "teamsize":
                teamSize[line.Arg(0)] = int.TryParse(line.Arg(1), out var size) ? size : 0;
                return (null, BattleCue.None, null);
            case "turn":
                Turn = int.TryParse(line.Arg(0), out var turn) ? turn : Turn;
                return (null, BattleCue.None, null);
            case "switch" or "drag":
            {
                var previous = active.GetValueOrDefault(side ?? "");
                SwitchIn(who, line.Arg(1), line.Arg(2));
                var mine = side == Viewer;
                var name = BattleProtocol.Who(who).Name;
                if (line.Kind == "drag")
                    return ($"{Mon(who)} est traîné de force au combat !", BattleCue.SwitchIn, side);
                var recall = previous is { Hp: > 0 } && previous.Name != name
                    ? mine ? $"{previous.Name}, reviens ! " : $"{TrainerName(false)} rappelle {previous.Name} ! "
                    : "";
                return (recall + (mine ? $"Go ! {name} !" : $"{TrainerName(false)} envoie {name} !"), BattleCue.SwitchIn, side);
            }
            case "move":
            {
                var move = BattleNames.Move(line.Arg(1));
                return ($"{Mon(who)} utilise {move} !", line.Has("still") ? BattleCue.None : BattleCue.Attack, side);
            }
            case "-damage":
            {
                SetHp(who, line.Arg(1));
                return (DamageText(who, from, of), BattleCue.Damage, side);
            }
            case "-heal":
            {
                SetHp(who, line.Arg(1));
                return (silent ? null : HealText(who, from, of, line.Tag("wisher")), BattleCue.Heal, side);
            }
            case "-sethp":
            {
                SetHp(who, line.Arg(1));
                var text = !painSplitShown && from == "move: Pain Split" ? "Les deux Pokémon partagent leurs PV !" : null;
                painSplitShown |= text is not null;
                return (text, BattleCue.Heal, side);
            }
            case "faint":
                Update(who, f => f with { Hp = 0 });
                if (side is not null)
                    fainted[side] = fainted.GetValueOrDefault(side) + 1;
                return ($"{Mon(who)} est K.O. !", BattleCue.Faint, side);
            case "-status":
                Update(who, f => f with { Status = line.Arg(1) });
                return (StatusText(who, line.Arg(1), from, of), BattleCue.None, side);
            case "-curestatus":
                Update(who, f => f with { Status = null });
                return (silent || !line.Has("msg") && Effect(from).Kind != "ability" ? null : CureText(who, line.Arg(1), from), BattleCue.None, side);
            case "-cureteam":
                foreach (var key in active.Keys.Where(k => k == side).ToList())
                    active[key] = active[key] is { } f ? f with { Status = null } : null;
                return (from == "move: Aromatherapy" ? "Une odeur apaisante se répand !" : "Un carillon retentit !", BattleCue.None, side);
            case "-boost" or "-unboost":
                return (silent ? null : BoostText(who, line.Arg(1), line.Arg(2), line.Kind == "-boost"), BattleCue.None, side);
            case "-setboost":
                return ($"{Mon(who)} sacrifie des PV et maximise son Attaque !", BattleCue.None, side);
            case "-clearallboost":
                return ("Les changements de stats sont annulés !", BattleCue.None, null);
            case "-clearnegativeboost":
                return (silent ? null : $"Les baisses de stats {De(Mon(who))} sont annulées !", BattleCue.None, side);
            case "-copyboost":
                return ($"{Mon(who)} copie les changements de stats {De(Mon(line.Arg(1)))} !", BattleCue.None, side);
            case "-weather":
                return (WeatherText(line.Arg(0), line.Has("upkeep"), from, of), BattleCue.None, null);
            case "-sidestart":
                return (SideStartText(line.Arg(0), EffectName(line.Arg(1)), Effect(line.Arg(1)).Name), BattleCue.None, null);
            case "-sideend":
                return (SideEndText(line.Arg(0), EffectName(line.Arg(1)), Effect(line.Arg(1)).Name), BattleCue.None, null);
            case "-start":
                return (silent ? null : StartText(line, who, of), BattleCue.None, side);
            case "-end":
                return (silent ? null : EndText(line, who), BattleCue.None, side);
            case "-activate":
                return (ActivateText(line, who, of), BattleCue.None, side);
            case "-singleturn":
                return (SingleTurnText(who, Effect(line.Arg(1)).Name), BattleCue.None, side);
            case "-singlemove":
                return (Effect(line.Arg(1)).Name switch
                {
                    "Destiny Bond" => $"{Mon(who)} veut emmener son adversaire avec lui !",
                    "Grudge" => $"{Mon(who)} veut que son adversaire lui garde rancune !",
                    _ => null, // Frénésie : rien en jeu
                }, BattleCue.None, side);
            case "-prepare":
                return (PrepareText(who, line.Arg(1)), BattleCue.None, side);
            case "-mustrecharge" or "-anim" or "-hint" or "-message" or "-center" or "-waiting":
                return (null, BattleCue.None, null);
            case "-crit":
                return ("Coup critique !", BattleCue.None, side);
            case "-supereffective":
                return ("C'est super efficace !", BattleCue.None, side);
            case "-resisted":
                return ("Ce n'est pas très efficace…", BattleCue.None, side);
            case "-immune":
                return (ImmuneText(who, line, from), BattleCue.None, side);
            case "-miss":
                return (line.Arg(1).Length > 0 ? $"{Mon(line.Arg(1))} évite l'attaque !" : $"L'attaque {De(Mon(who))} échoue !", BattleCue.None, side);
            case "-fail":
                return (FailText(line, who, from), BattleCue.None, side);
            case "-notarget":
                return ("Mais il n'y a pas de cible…", BattleCue.None, side);
            case "-nothing":
                return ("Mais rien ne se passe !", BattleCue.None, null);
            case "-ohko":
                return ("C'est un K.O. en un coup !", BattleCue.None, null);
            case "-hitcount":
                return ($"Touché {line.Arg(1)} fois !", BattleCue.None, side);
            case "-item":
                return (silent ? null : ItemText(who, BattleNames.Item(line.Arg(1)), from, of), BattleCue.None, side);
            case "-enditem":
                return (silent ? null : EndItemText(who, line, from, of), BattleCue.None, side);
            case "-ability":
                return (silent ? null : AbilityText(who, line, from, of), BattleCue.None, side);
            case "-transform":
                return ($"{Mon(who)} se transforme en {BattleProtocol.Who(line.Arg(1)).Name} !", BattleCue.None, side);
            case "-formechange":
            {
                var (_, forme, _) = BattleNames.SpeciesOf(line.Arg(1));
                Update(who, f => f with { Forme = forme });
                return (silent ? null : $"{Mon(who)} change de forme !", BattleCue.None, side);
            }
            case "-fieldactivate":
                return (Effect(line.Arg(0)).Name switch
                {
                    "Pay Day" => "Des pièces sont éparpillées partout !",
                    "Perish Song" => "Tous les Pokémon qui entendent le Requiem seront K.O. dans trois tours !",
                    _ => null,
                }, BattleCue.None, null);
            case "cant":
                return (CantText(who, line.Arg(1), line.Arg(2)), BattleCue.None, side);
            case "win":
                Outcome = line.Arg(0) == trainers.GetValueOrDefault(Viewer) ? BattleOutcome.Won : BattleOutcome.Lost;
                return (Outcome == BattleOutcome.Won ? $"Tu as battu {TrainerName(false)} !" : $"{TrainerName(false)} a gagné le combat !", BattleCue.None, null);
            case "tie":
                Outcome = BattleOutcome.Draw;
                return ("Match nul !", BattleCue.None, null);
            default:
                Unhandled.Add(string.Join('|', [line.Kind, .. line.Args]));
                return (null, BattleCue.None, null);
        }
    }

    private string? DamageText(string who, string? from, string? of)
    {
        if (from is null)
            return null;
        var (kind, name) = Effect(from);
        return (kind, name) switch
        {
            ("", "brn") => $"{Mon(who)} souffre de sa brûlure !",
            ("", "psn" or "tox") => $"{Mon(who)} souffre du poison !",
            ("", "Leech Seed") => $"Vampigraine draine l'énergie {De(Mon(who))} !",
            ("", "Sandstorm") => $"La tempête de sable frappe {Mon(who)} !",
            ("", "Hail") => $"La grêle frappe {Mon(who)} !",
            ("", "Recoil") => $"{Mon(who)} est blessé par le contrecoup !",
            ("", "confusion") => "Il se blesse dans sa confusion !",
            ("", "Spikes") => $"{Mon(who)} est blessé par les Picots !",
            ("", "Curse") => $"{Mon(who)} est touché par la malédiction !",
            ("", "Nightmare") => $"{Mon(who)} est pris dans un cauchemar !",
            ("ability", "Liquid Ooze") => $"{Mon(who)} aspire le Suintement !",
            ("ability", _) => $"{Mon(who)} est blessé par {BattleNames.Ability(name)} {De(Mon(of ?? who))} !",
            ("item", _) => $"{Mon(who)} est blessé par {BattleNames.Item(name)} !",
            ("move", _) => $"{Mon(who)} est blessé par {BattleNames.Move(name)} !",
            _ => $"{Mon(who)} est blessé !",
        };
    }

    private string HealText(string who, string? from, string? of, string? wisher)
    {
        var (kind, name) = Effect(from);
        return (kind, name) switch
        {
            ("", "") => $"{Mon(who)} récupère des PV !",
            ("", "drain") => $"L'énergie {De(Mon(of ?? who))} est drainée !",
            ("", "Ingrain") => $"{Mon(who)} absorbe des nutriments avec ses racines !",
            ("move", "Wish") => $"Le vœu {De(wisher ?? BattleProtocol.Who(who).Name)} se réalise !",
            ("item", _) when name.EndsWith("Berry", StringComparison.Ordinal) => $"{Mon(who)} récupère des PV !",
            ("item", _) => $"{Mon(who)} récupère des PV grâce à {BattleNames.Item(name)} !",
            ("ability", _) => $"{BattleNames.Ability(name)} {De(Mon(who))} lui rend des PV !",
            _ => $"{Mon(who)} récupère des PV !",
        };
    }

    private string StatusText(string who, string status, string? from, string? of)
    {
        var (kind, name) = Effect(from);
        if (kind == "move" && name == "Rest")
            return $"{Mon(who)} s'endort et récupère des PV !";
        if (kind == "ability")
            return $"{Mon(who)} est {StatusAdjective(status)} par {BattleNames.Ability(name)} {De(Mon(of ?? who))} !";
        return status switch
        {
            "brn" => $"{Mon(who)} est brûlé !",
            "par" => $"{Mon(who)} est paralysé ! Il aura du mal à attaquer !",
            "slp" => $"{Mon(who)} s'endort !",
            "frz" => $"{Mon(who)} est gelé !",
            "psn" => $"{Mon(who)} est empoisonné !",
            "tox" => $"{Mon(who)} est gravement empoisonné !",
            _ => $"{Mon(who)} est {StatusAdjective(status)} !",
        };
    }

    private string CureText(string who, string status, string? from)
    {
        var prefix = Effect(from) is ("ability", var ability) ? $"{BattleNames.Ability(ability)} {De(Mon(who))} ! " : "";
        return prefix + status switch
        {
            "slp" => $"{Mon(who)} se réveille !",
            "frz" => $"{Mon(who)} dégèle !",
            "par" => $"{Mon(who)} n'est plus paralysé !",
            "brn" => $"{Mon(who)} n'est plus brûlé !",
            _ => $"{Mon(who)} n'est plus empoisonné !",
        };
    }

    private string BoostText(string who, string stat, string amount, bool up)
    {
        var (name, article) = Stats.GetValueOrDefault(stat, (stat, "Le "));
        var n = int.TryParse(amount, out var parsed) ? parsed : 1;
        var subject = $"{article}{name} {De(Mon(who))}";
        return n switch
        {
            0 => $"{subject} ne peut plus {(up ? "augmenter" : "baisser")} !",
            1 => $"{subject} {(up ? "augmente" : "baisse")} !",
            2 => $"{subject} {(up ? "augmente" : "baisse")} beaucoup !",
            _ => $"{subject} {(up ? "augmente" : "baisse")} énormément !",
        };
    }

    private string? WeatherText(string kind, bool upkeep, string? from, string? of)
    {
        var previous = weather;
        weather = kind == "none" ? null : kind;
        if (kind == "none")
        {
            return previous switch
            {
                "RainDance" => "La pluie s'arrête.",
                "SunnyDay" => "Le soleil redevient normal.",
                "Sandstorm" => "La tempête de sable se calme.",
                "Hail" => "La grêle s'arrête.",
                _ => null,
            };
        }
        if (upkeep)
        {
            return kind switch
            {
                "RainDance" => "La pluie continue de tomber.",
                "SunnyDay" => "Le soleil brille fort.",
                "Sandstorm" => "La tempête de sable fait rage.",
                "Hail" => "La grêle continue de tomber.",
                _ => null,
            };
        }
        var prefix = Effect(from) is ("ability", var ability) ? $"{BattleNames.Ability(ability)} {De(Mon(of ?? ""))} ! " : "";
        return prefix + kind switch
        {
            "RainDance" => "Il commence à pleuvoir !",
            "SunnyDay" => "Le soleil brille !",
            "Sandstorm" => "Une tempête de sable se lève !",
            "Hail" => "Il commence à grêler !",
            _ => "Le temps change !",
        };
    }

    private string SideStartText(string sideArg, string effect, string english) => english switch
    {
        "Reflect" => $"{effect} renforce la Défense {De(Team(sideArg))} !",
        "Light Screen" => $"{effect} renforce la Défense Spéciale {De(Team(sideArg))} !",
        "Spikes" => $"Des {effect} s'éparpillent autour {De(Team(sideArg))} !",
        "Mist" => $"{Capital(Team(sideArg))} est enveloppée de {effect} !",
        "Safeguard" => $"{Capital(Team(sideArg))} est protégée par {effect} !",
        _ => $"{effect} agit sur {Team(sideArg)} !",
    };

    private string SideEndText(string sideArg, string effect, string english) => english switch
    {
        "Spikes" => $"Les {effect} autour {De(Team(sideArg))} disparaissent !",
        _ => $"L'effet de {effect} sur {Team(sideArg)} prend fin !",
    };

    private string? StartText(BattleLine line, string who, string? of)
    {
        var effect = line.Arg(1);
        var (kind, name) = Effect(effect);
        if (effect.StartsWith("perish", StringComparison.Ordinal))
            return $"Le compte à rebours {De(Mon(who))} passe à {effect["perish".Length..]} !";
        if (effect.StartsWith("stockpile", StringComparison.Ordinal))
            return $"{Mon(who)} a stocké {effect["stockpile".Length..]} fois !";
        return (kind, name) switch
        {
            (_, "confusion") => line.Has("fatigue") ? $"{Mon(who)} est épuisé et devient confus !" : $"{Mon(who)} devient confus !",
            (_, "Substitute") => $"{Mon(who)} crée un clone !",
            (_, "Leech Seed") => $"{Mon(who)} est infecté par Vampigraine !",
            (_, "Disable") => $"{BattleNames.Move(line.Arg(2))} {De(Mon(who))} est bloquée !",
            (_, "Encore") => $"{Mon(who)} subit l'effet d'Encore !",
            (_, "Taunt") => $"{Mon(who)} est provoqué !",
            (_, "Torment") => $"{Mon(who)} est tourmenté !",
            (_, "Yawn") => $"{Mon(who)} commence à somnoler…",
            (_, "Focus Energy") => $"{Mon(who)} se concentre !",
            (_, "Attract") when Effect(line.Tag("from")).Kind == "ability" =>
                $"{Mon(who)} tombe amoureux {De(Mon(of ?? who))} grâce à {BattleNames.Ability(Effect(line.Tag("from")).Name)} !",
            (_, "Attract") => $"{Mon(who)} tombe amoureux !",
            (_, "Curse") => $"{Mon(of ?? who)} sacrifie des PV et maudit {Mon(who)} !",
            (_, "Nightmare") => $"{Mon(who)} fait un cauchemar !",
            (_, "Foresight") => $"{Mon(who)} est identifié !",
            (_, "Bide") => $"{Mon(who)} accumule de l'énergie !",
            (_, "Uproar") => line.Has("upkeep") ? $"{Mon(who)} fait toujours un brouhaha !" : $"{Mon(who)} provoque un brouhaha !",
            (_, "Charge") => $"{Mon(who)} se charge d'électricité !",
            (_, "Ingrain") => $"{Mon(who)} plante ses racines !",
            (_, "Imprison") => $"{Mon(who)} scelle les capacités de son adversaire !",
            (_, "Mud Sport") => "L'électricité perd de sa puissance !",
            (_, "Water Sport") => "Le feu perd de sa puissance !",
            (_, "Future Sight") => $"{Mon(who)} prévoit une attaque !",
            (_, "Doom Desire") => $"{Mon(who)} choisit {BattleNames.Move("Doom Desire")} comme destin !",
            (_, "typechange") => $"{Mon(who)} devient de type {BattleNames.Type(line.Arg(2))} !",
            ("ability", "Flash Fire") => $"{BattleNames.Ability(name)} {De(Mon(who))} renforce ses attaques Feu !",
            _ => Unknown(line),
        };
    }

    private string? EndText(BattleLine line, string who)
    {
        var (_, name) = Effect(line.Arg(1));
        return name switch
        {
            "confusion" => $"{Mon(who)} n'est plus confus !",
            "Substitute" => $"Le clone {De(Mon(who))} disparaît…",
            "Disable" => $"{Mon(who)} n'est plus entravé !",
            "Encore" => $"L'effet d'Encore sur {Mon(who)} prend fin !",
            "Taunt" => $"{Mon(who)} n'est plus provoqué !",
            "Attract" => $"{Mon(who)} n'est plus amoureux !",
            "Bind" or "Wrap" or "Clamp" or "Fire Spin" or "Whirlpool" or "Sand Tomb" => $"{Mon(who)} est libéré de {BattleNames.Move(name)} !",
            "Nightmare" => $"{Mon(who)} ne fait plus de cauchemar !",
            "Uproar" => $"{Mon(who)} se calme !",
            "Bide" => $"{Mon(who)} libère l'énergie accumulée !",
            "Future Sight" => $"{Mon(who)} subit l'attaque {BattleNames.Move(name)} !",
            "Doom Desire" => $"{Mon(who)} subit {BattleNames.Move(name)} !",
            "Charge" or "Yawn" or "Focus Energy" or "Leech Seed" or "Ingrain" or "Foresight" or "Torment" or "Mud Sport"
                or "Water Sport" or "Imprison" or "Curse" or "typechange" or "Flash Fire" => null,
            _ when name.StartsWith("stockpile", StringComparison.Ordinal) || name.StartsWith("perish", StringComparison.Ordinal) => null,
            _ => Unknown(line),
        };
    }

    private string? ActivateText(BattleLine line, string who, string? of)
    {
        var (kind, name) = Effect(line.Arg(1));
        var other = of is null ? "" : Mon(of);
        return (kind, name) switch
        {
            (_, "Protect") => $"{Mon(who)} se protège !",
            (_, "Substitute") => $"Le clone encaisse les dégâts à la place {De(Mon(who))} !",
            (_, "confusion") => $"{Mon(who)} est confus !",
            (_, "trapped") => $"{Mon(who)} ne peut plus s'enfuir !",
            (_, "Attract") => $"{Mon(who)} est amoureux {De(other)} !",
            (_, "Beat Up") => $"Attaque {De(of ?? "")} !",
            (_, "Bide") => $"{Mon(who)} accumule de l'énergie !",
            (_, "Bind") => $"{Mon(who)} est ligoté par {other} !",
            (_, "Wrap") => $"{Mon(who)} est ligoté par {other} !",
            (_, "Clamp") => $"{other} serre {Mon(who)} dans sa coquille !",
            (_, "Fire Spin") => $"{Mon(who)} est piégé dans le tourbillon de flammes !",
            (_, "Whirlpool") => $"{Mon(who)} est piégé dans le tourbillon !",
            (_, "Sand Tomb") => $"{Mon(who)} est piégé par {BattleNames.Move(name)} !",
            (_, "Destiny Bond") => $"{Mon(who)} emmène son adversaire avec lui !",
            (_, "Endure") => $"{Mon(who)} tient bon !",
            (_, "Grudge") => $"{BattleNames.Move(line.Arg(2))} {De(Mon(who))} perd tous ses PP à cause de la rancune !",
            (_, "Heal Bell") => "Un carillon retentit !",
            (_, "Ingrain") => $"{Mon(who)} est retenu par ses racines !",
            (_, "Lock-On" or "Mind Reader") => $"{Mon(who)} vise {other} !",
            (_, "Magnitude") => $"Ampleur {line.Arg(2)} !",
            (_, "Mimic") => $"{Mon(who)} apprend {BattleNames.Move(line.Arg(2))} !",
            (_, "Mist") => $"{Mon(who)} est protégé par la Brume !",
            (_, "Safeguard") => $"{Mon(who)} est protégé par Rune Protect !",
            (_, "Snatch") => $"{Mon(who)} vole la capacité {De(other)} !",
            (_, "Spite") => $"Dépit fait perdre {line.Arg(3)} PP à {BattleNames.Move(line.Arg(2))} {De(Mon(who))} !",
            (_, "Trick") => $"{Mon(who)} échange son objet avec {other} !",
            (_, "Skill Swap") => $"{Mon(who)} échange son talent avec {(other.Length > 0 ? other : "son adversaire")} !",
            (_, "Pain Split") => "Les deux Pokémon partagent leurs PV !",
            (_, "Pursuit") => $"{Mon(who)} est rattrapé par {BattleNames.Move(name)} avant de revenir !",
            ("item", _) when line.Arg(2).Length > 0 =>
                $"{Mon(who)} restaure les PP de {BattleNames.Move(line.Arg(2))} avec {BattleNames.Item(name)} !",
            ("item", _) => $"{BattleNames.Item(name)} {De(Mon(who))} agit !",
            ("ability", "Suction Cups") => $"{Mon(who)} s'accroche avec {BattleNames.Ability(name)} !",
            ("ability", _) => $"{BattleNames.Ability(name)} {De(Mon(who))} !",
            _ => Unknown(line),
        };
    }

    private string? SingleTurnText(string who, string effect) => effect switch
    {
        "Protect" => $"{Mon(who)} se protège !",
        "Endure" => $"{Mon(who)} se prépare à encaisser les coups !",
        "Focus Punch" => $"{Mon(who)} se concentre au maximum !",
        "Magic Coat" => $"{Mon(who)} s'entoure d'un reflet magique !",
        "Snatch" => $"{Mon(who)} attend que son adversaire agisse !",
        _ => null,
    };

    private string PrepareText(string who, string move) => move switch
    {
        "Solar Beam" => $"{Mon(who)} absorbe la lumière !",
        "Fly" => $"{Mon(who)} s'envole !",
        "Dig" => $"{Mon(who)} creuse un trou !",
        "Dive" => $"{Mon(who)} plonge !",
        "Bounce" => $"{Mon(who)} bondit !",
        "Razor Wind" => $"{Mon(who)} crée un tourbillon !",
        "Skull Bash" => $"{Mon(who)} baisse la tête !",
        "Sky Attack" => $"{Mon(who)} est entouré d'une lumière intense !",
        _ => $"{Mon(who)} se prépare à attaquer !",
    };

    private string ImmuneText(string who, BattleLine line, string? from)
    {
        if (Effect(from) is ("ability", var ability))
            return $"Ça n'affecte pas {Mon(who)} grâce à {BattleNames.Ability(ability)} !";
        if (line.Arg(1) == "confusion")
            return $"{Mon(who)} ne peut pas être confus !";
        return $"Ça n'affecte pas {Mon(who)}…";
    }

    private string FailText(BattleLine line, string who, string? from)
    {
        var what = line.Arg(1);
        if (Effect(from) is ("ability", var ability))
        {
            return what == "unboost"
                ? $"{BattleNames.Ability(ability)} {De(Mon(who))} empêche la baisse de ses stats !"
                : $"{BattleNames.Ability(ability)} {De(Mon(who))} l'en empêche !";
        }
        return what switch
        {
            "brn" or "par" or "slp" or "frz" or "psn" or "tox" => $"{Mon(who)} est déjà {StatusAdjective(what)} !",
            "heal" => $"Les PV {De(Mon(who))} sont déjà au maximum !",
            "unboost" => $"Les stats {De(Mon(who))} ne baissent pas !",
            "move: Substitute" => line.Has("weak") ? $"{Mon(who)} n'a pas assez de PV pour créer un clone !" : $"{Mon(who)} a déjà un clone !",
            _ => "Mais cela échoue !",
        };
    }

    private string? ItemText(string who, string item, string? from, string? of) => Effect(from) switch
    {
        (_, "Trick") => $"{Mon(who)} obtient {item} !",
        (_, "Thief" or "Covet") => $"{Mon(who)} vole {item} {De(Mon(of ?? who))} !",
        (_, "Recycle") => $"{Mon(who)} recycle {item} !",
        _ => null,
    };

    private string? EndItemText(string who, BattleLine line, string? from, string? of)
    {
        var item = BattleNames.Item(line.Arg(1));
        if (line.Has("eat"))
            return $"{Mon(who)} mange {item} !";
        return Effect(from) switch
        {
            (_, "Knock Off") => $"{Mon(of ?? who)} fait tomber {item} {De(Mon(who))} !",
            (_, "Thief" or "Covet" or "Trick") => null,
            _ when line.Arg(1) == "White Herb" => $"{Mon(who)} retrouve ses stats grâce à {item} !",
            _ when line.Arg(1) == "Mental Herb" => $"{Mon(who)} retrouve ses esprits grâce à {item} !",
            _ => $"{Mon(who)} utilise {item} !",
        };
    }

    private string? AbilityText(string who, BattleLine line, string? from, string? of)
    {
        var ability = BattleNames.Ability(line.Arg(1));
        return Effect(from) switch
        {
            ("ability", "Trace") => $"{Mon(who)} copie {ability} {De(Mon(of ?? who))} grâce à {BattleNames.Ability("Trace")} !",
            ("move", "Role Play") => $"{Mon(who)} copie le talent {ability} {De(Mon(of ?? who))} !",
            ("move", "Skill Swap") => $"{Mon(who)} obtient le talent {ability} !",
            _ when line.Arg(1) == "Pressure" => $"{Mon(who)} exerce une pression !",
            _ => $"{ability} {De(Mon(who))} !",
        };
    }

    private string CantText(string who, string reason, string move)
    {
        var (_, name) = Effect(reason);
        return name switch
        {
            "slp" => $"{Mon(who)} dort profondément.",
            "frz" => $"{Mon(who)} est gelé !",
            "par" => $"{Mon(who)} est paralysé ! Il ne peut pas attaquer !",
            "recharge" => $"{Mon(who)} doit se reposer !",
            "flinch" => $"{Mon(who)} a peur ! Il ne peut pas attaquer !",
            "Attract" => $"{Mon(who)} est trop amoureux pour attaquer !",
            "Truant" => $"{Mon(who)} paresse !",
            "Disable" => $"{BattleNames.Move(move)} {De(Mon(who))} est bloquée !",
            "Taunt" => $"{Mon(who)} ne peut pas utiliser {BattleNames.Move(move)} à cause de la provocation !",
            "Imprison" => $"{Mon(who)} ne peut pas utiliser {BattleNames.Move(move)} : elle est scellée !",
            "Focus Punch" => $"{Mon(who)} perd sa concentration et ne peut pas attaquer !",
            "nopp" => "Il n'y a plus de PP pour cette capacité !",
            _ => $"{Mon(who)} ne peut pas attaquer !",
        };
    }

    private string? Unknown(BattleLine line)
    {
        Unhandled.Add(string.Join('|', [line.Kind, .. line.Args]));
        return null;
    }
}
