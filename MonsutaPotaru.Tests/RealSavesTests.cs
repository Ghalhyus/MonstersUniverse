using Microsoft.EntityFrameworkCore;
using PKHeX.Core;
using MonsutaPotaru.Data;
using MonsutaPotaru.Services;

namespace MonsutaPotaru.Tests;

/// <summary>
/// Vraies sauvegardes de jeu, déposées dans <c>MonsutaPotaru.Tests/Sauvegardes/</c> (versionnées malgré la règle *.sav) :
/// chaque fichier du dossier passe tout le parcours du site. Sans fichier, ces tests ne vérifient rien.
/// </summary>
public sealed class RealSavesTests : IDisposable
{
    private const string Owner = "sacha";
    private readonly TestDatabase _db = new(Owner);

    public void Dispose() => _db.Dispose();

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Sauvegardes");

    /// <summary>Noms des fichiers du dossier ; « » seul quand il n'y en a pas (xUnit refuse une liste vide).</summary>
    public static TheoryData<string> Files()
    {
        var files = Directory.Exists(Folder) ? Directory.GetFiles(Folder).Select(Path.GetFileName).OfType<string>().Order().ToList() : [];
        return new TheoryData<string>(files.Count > 0 ? files : [""]);
    }

    /// <summary>Contenu du fichier, null pour la ligne vide de <see cref="Files"/>.</summary>
    private static byte[]? Read(string file) => file == "" ? null : File.ReadAllBytes(Path.Combine(Folder, file));

    private static SaveFile Open(byte[] data, string file)
    {
        // PKHeX écrit dans le tableau qu'on lui donne : une copie laisse l'original intact.
        Assert.True(SaveUtil.TryGetSaveFile(data.ToArray(), out var sav, file), $"{file} : illisible par PKHeX");
        return sav!;
    }

    private async Task<int> Store(string file, byte[] data)
    {
        var result = await _db.StoredSaves().AddAsync(Owner, file, data);
        Assert.True(result.Ok, $"{file} : {result.Message}");
        return (await _db.StoredSaves().ListAsync(Owner)).Single().Id;
    }

    private async Task<SaveFile> Stored(int saveId, string file)
    {
        using var db = _db.NewContext();
        return Open((await db.StoredSaves.SingleAsync(s => s.Id == saveId)).Data, file);
    }

    /// <summary>Emplacements remplis de toutes les poches (sac et PC) : poche, numéro d'objet dans le jeu, quantité.</summary>
    private static List<(InventoryType, int, int)> Slots(SaveFile sav) =>
        sav.Inventory.Pouches.SelectMany(p => p.Items.Where(i => i.Index != 0 && i.Count > 0).Select(i => (p.Type, (int)i.Index, i.Count))).ToList();

    /// <summary>Chaque élément de <paramref name="part"/> est dans <paramref name="whole"/> (multi-ensembles).</summary>
    private static void AssertContained(List<(InventoryType, int, int)> part, List<(InventoryType, int, int)> whole, string file)
    {
        var left = whole.ToList();
        foreach (var slot in part)
            Assert.True(left.Remove(slot), $"{file} : {slot} a changé");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Save_IsRead_AndRewrittenIdentically(string file)
    {
        if (Read(file) is not { } data)
            return;
        var sav = Open(data, file);
        // 4e génération : les blocs annexes (Tour de Combat, vidéos de combat...) que le jeu n'a jamais écrits restent à zéro,
        // sans somme de contrôle valide (soulsilver.sav) ; seuls les deux blocs principaux comptent.
        Assert.True(sav.ChecksumsValid || sav is SAV4 && !sav.ChecksumInfo.Contains("block checksum is invalid"),
            $"{file} : {sav.ChecksumInfo}");
        var written = sav.Write().ToArray();
        // 1re et 2e génération : le jeu ne prépare ses boîtes qu'au premier changement de boîte (zone à FF jusque-là) et
        // garde la boîte en cours à part ; PKHeX écrit des boîtes vides en règle et resynchronise la boîte en cours.
        // Le fichier change donc, pas son contenu.
        // HeartGold / SoulSilver : PKHeX marque toutes les boîtes comme modifiées (le jeu n'enregistre que celles-là), et
        // donne leur somme de contrôle aux blocs annexes vides ; le fichier change la première fois, puis plus.
        if (sav is SAV4HGSS)
            Assert.True(Open(written.ToArray(), file).Write().Span.SequenceEqual(written), $"{file} : réécrit différemment par PKHeX");
        else if (sav.Generation >= 3)
            Assert.True(written.AsSpan().SequenceEqual(data), $"{file} : réécrit différemment par PKHeX");
        var again = Open(written, file);
        Assert.Equal(SaveIdentity.Of(sav).Key, SaveIdentity.Of(again).Key);
        Assert.Equal(sav.Money, again.Money);
        Assert.Equal(Contents(sav), Contents(again));
        Assert.Null(SaveBag.Unsupported(sav));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Items_AllHaveAnImage(string file)
    {
        if (Read(file) is not { } data)
            return;
        var missing = SaveBag.ReadItems(Open(data, file)).Keys
            .Where(item => !File.Exists(Path.Combine(WebRootWithImages(), PokemonInfo.ItemSpriteUrl(item))))
            .ToList();
        Assert.True(missing.Count == 0, $"{file} : objets sans image (outils/images.py) : {string.Join(", ", missing)}");
    }

    /// <summary>Images du site (outils/images.py) : chaque espèce connue de PKHeX a la sienne, normale et chromatique.</summary>
    [Fact]
    public void Pokemon_AllSpeciesHaveAnImage()
    {
        var missing = Enumerable.Range(1, (int)Species.MAX_COUNT - 1)
            .SelectMany(s => new[] { false, true }.Select(shiny => PokemonSprites.Url((ushort)s, 0, EntityContext.Gen9, false, 0, shiny)))
            .Where(url => !File.Exists(Path.Combine(WebRootWithImages(), url)))
            .ToList();
        Assert.True(missing.Count == 0, $"images manquantes (outils/images.py) : {string.Join(", ", missing.Take(10))}");
    }

    /// <summary>Dossier wwwroot du site, retrouvé depuis ce fichier source (les tests peuvent être compilés ailleurs).</summary>
    internal static string WebRoot([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "..", "MonsutaPotaru", "wwwroot");

    /// <summary>
    /// <see cref="WebRoot"/>, en échouant avec un message clair si les images n'y sont pas : elles ne sont pas dans le dépôt
    /// Git (stockées sur le serveur), il faut les récupérer après avoir cloné.
    /// </summary>
    internal static string WebRootWithImages([System.Runtime.CompilerServices.CallerFilePath] string source = "")
    {
        var root = WebRoot(source);
        Assert.True(Directory.Exists(Path.Combine(root, "images", "pokemon")) && Directory.Exists(Path.Combine(root, "images", "objets")),
            @"Images absentes de MonsutaPotaru\wwwroot\images : lance outils\recuperer-images.cmd debian@IP_DU_VPS (ou python outils/images.py).");
        return root;
    }

    /// <summary>Octets de chaque Pokémon de l'équipe et des boîtes, dans l'ordre.</summary>
    private static List<string> Contents(SaveFile sav) =>
        sav.PartyData.Concat(Enumerable.Range(0, sav.BoxCount).SelectMany(sav.GetBoxData))
            .Select(pk => pk.Species == 0 ? "" : Convert.ToHexString(BankService.StoredData(pk))).ToList();

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Transfer_DepositsAPokemon_ThenWithdrawsItBack(string file)
    {
        if (Read(file) is not { } data)
            return;
        var session = new SaveSession();
        Assert.True(session.TryLoad(data.ToArray(), file, out var error), $"{file} : {error}");
        var before = Boxed(session.Save!);
        var (box, slot) = Enumerable.Range(0, session.Save!.BoxCount)
            .SelectMany(b => Enumerable.Range(0, session.Save.BoxSlotCount).Select(s => (b, s)))
            .First(p => session.GetSlot(p.b, p.s) is not null && !session.IsLocked(p.b, p.s));
        var original = Key(session.GetSlot(box, slot)!);
        Assert.True(session.TryDeposit(box, slot, out error), $"{file} : {error}");
        var afterDeposit = await _db.Bank().CommitAsync(Owner, session);

        var deposited = Boxed(Open(afterDeposit, file));
        Assert.Equal(before.Count - 1, deposited.Count);
        Assert.Equal(before.Count(k => k == original) - 1, deposited.Count(k => k == original));
        var entry = (await _db.Bank().ListAsync(Owner)).Single();
        Assert.Equal(original, Key(BankService.ToPkm(entry)!));

        // Retour à la même case. En 1re et 2e génération, une boîte est une liste sans trou, tassée à l'écriture (comme
        // le fait le jeu) : la case s'est remplie avec le Pokémon suivant, il revient donc à la première case libre.
        session = new SaveSession();
        Assert.True(session.TryLoad(afterDeposit, file, out error), $"{file} : {error}");
        var target = session.GetSlot(box, slot) is null ? (box, slot) : session.FindEmptySlot(box, 0);
        Assert.True(session.TryWithdraw(entry.Id, BankService.ToPkm(entry)!, target.Item1, target.Item2, out error), $"{file} : {error}");
        var back = Open(await _db.Bank().CommitAsync(Owner, session), file);
        Assert.Equal(before.Order(), Boxed(back).Order());
        if (back.Generation >= 3)
            Assert.Equal(original, Key(back.GetBoxSlotAtIndex(box, slot)));
        Assert.Empty(await _db.Bank().ListAsync(Owner));
    }

    /// <summary>Ce qui distingue un Pokémon (espèce, PID, constante de chiffrement, expérience, IV, dresseur d'origine).</summary>
    private static string Key(PKM pk) =>
        $"{pk.Species}|{pk.PID}|{pk.EncryptionConstant}|{pk.EXP}|{pk.IVTotal}|{pk.OriginalTrainerName}";

    /// <summary>Pokémon des boîtes, quel que soit leur emplacement.</summary>
    private static List<string> Boxed(SaveFile sav) =>
        Enumerable.Range(0, sav.BoxCount).SelectMany(sav.GetBoxData).Where(pk => pk.Species != 0).Select(Key).ToList();

    [Theory]
    [MemberData(nameof(Files))]
    public async Task StoredSave_ShowsAllItsPokemon_AndLetsMoneyBeWithdrawn(string file)
    {
        if (Read(file) is not { } data)
            return;
        var sav = Open(data, file);
        var inBoxes = Enumerable.Range(0, sav.BoxCount).Sum(b => sav.GetBoxData(b).Count(pk => pk.Species != 0));
        var id = await Store(file, data);
        var stored = (await _db.StoredSaves().ListAsync(Owner)).Single();
        Assert.True(stored.PokemonCount >= sav.PartyCount + inBoxes, $"{file} : {stored.PokemonCount} Pokémon lus");
        Assert.Equal(stored.PokemonCount, await _db.Bank().CountAsync(Owner, filter: CollectionFilter.Save(id)));

        var result = await _db.Inventory().WithdrawFromSaveAsync(Owner, id, 1000);
        Assert.True(result.Ok, $"{file} : {result.Message}");
        Assert.Equal(sav.Money - 1000, (await Stored(id, file)).Money);
        Assert.Equal(1000, await _db.Inventory().GetBalanceAsync(Owner));
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Items_AllWithdrawn_ThenAllDepositedBack(string file)
    {
        if (Read(file) is not { } data)
            return;
        var id = await Store(file, data);
        var before = await Stored(id, file);
        var bag = SaveBag.ReadItems(before, BagPlace.Bag);
        var pc = SaveBag.ReadItems(before, BagPlace.Pc); // PC d'objets : 1re à 3e génération
        Assert.NotEmpty(SaveBag.ReadItems(before));
        Assert.Equal(SaveBag.HasPc(before), before.Generation <= 3);

        var withdrawn = await _db.Inventory().WithdrawItemsAsync(Owner, id, bag, pc);
        Assert.True(withdrawn.Ok, $"{file} : {withdrawn.Message}");
        var emptied = await Stored(id, file);
        Assert.Empty(SaveBag.ReadItems(emptied));
        // Ce qui reste (objets rares, CS, Lettres...) n'a pas bougé d'un exemplaire.
        var untouched = Slots(emptied);
        AssertContained(untouched, Slots(before), file);
        Assert.Equal(bag.Values.Sum() + pc.Values.Sum(), (await _db.Inventory().GetItemsAsync(Owner)).Sum(i => i.Count));

        var deposited = await _db.Inventory().DepositItemsAsync(Owner, id, bag, pc);
        Assert.True(deposited.Ok, $"{file} : {deposited.Message}");
        var after = await Stored(id, file);
        Assert.Equal(bag.OrderBy(i => i.Key), SaveBag.ReadItems(after, BagPlace.Bag).OrderBy(i => i.Key));
        Assert.Equal(pc.OrderBy(i => i.Key), SaveBag.ReadItems(after, BagPlace.Pc).OrderBy(i => i.Key));
        AssertContained(untouched, Slots(after), file);
        Assert.Empty(await _db.Inventory().GetItemsAsync(Owner));
    }
}
