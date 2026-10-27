// Pont entre Monsuta Potaru et le simulateur de Pokémon Showdown (@pkmn/sim) : un processus par combat, lancé par le site
// (Services/Battle/ShowdownBattle.cs). Échanges par lignes, sur l'entrée et la sortie standard :
// - 1re ligne reçue, en JSON : {"format", "seed", "p1": {"name", "team"}, "p2": {...}} ; chaque Pokémon de "team" est donné par
//   numéros (espèce, objet, talent, capacités : numérotation nationale, celle de PKHeX), traduits ici en noms de Showdown ;
// - lignes suivantes : les choix des joueurs (« p1 move 2 », « p2 switch 3 ») ou la fin forcée (« forcelose p1 »,
//   « forcetie ») ; tout le reste est refusé (le simulateur accepte aussi « eval » : jamais transmis) ;
// - lignes envoyées : chaque morceau du protocole de Showdown (« update », « sideupdate », « end »), encodé en JSON.
'use strict';
const {Battle, BattleStreams, Dex} = require('@pkmn/sim');
const readline = require('readline');

// PP Plus de chaque capacité (« ppUps » du Pokémon) : Showdown en suppose toujours 3. Appliqués juste avant le début du
// combat, quand les Pokémon existent et qu'aucune requête n'est encore partie.
const start = Battle.prototype.start;
Battle.prototype.start = function (...args) {
  for (const side of this.sides) {
    for (const pokemon of side.pokemon) {
      const ups = pokemon.set.ppUps;
      if (!Array.isArray(ups)) continue;
      pokemon.baseMoveSlots.forEach((slot, i) => {
        const move = this.dex.moves.get(slot.id);
        const count = move.noPPBoosts ? 0 : Math.max(0, Math.min(3, Number(ups[i]) || 0));
        pokemon.ppUps[i] = count;
        slot.pp = slot.maxpp = this.calculatePP(move, count);
      });
    }
  }
  return start.apply(this, args);
};

/**
 * Élément de Showdown de ce numéro : celui de la génération du combat (pas un objet « Past » de la 2e génération qui partage
 * son numéro, comme « Berry » et « Oran Berry »), puis le plus court identifiant (« hiddenpower », pas « hiddenpowerfire »).
 */
function byNum(list, num) {
  let best = null;
  const rank = entry => (entry.isNonstandard ? 1 : 0);
  for (const entry of list) {
    if (entry.num !== num) continue;
    if (!best || rank(entry) < rank(best) || (rank(entry) === rank(best) && entry.id.length < best.id.length)) best = entry;
  }
  return best;
}

/** Pokémon du site (numéros) → Pokémon de Showdown (noms). */
function toSet(dex, input) {
  const forms = dex.species.all().filter(s => s.num === input.species);
  let species = forms.find(s => input.forme ? s.forme === input.forme : !s.forme) ?? forms[0];
  if (!species) throw new Error(`espèce ${input.species} inconnue`);
  // Forme d'aspect (lettre de Zarbi : « Unown-B ») : absente de la liste des espèces, mais connue de Showdown, qui la montre
  // dans la description du Pokémon (image du bon Zarbi).
  const cosmetic = input.forme && species.forme !== input.forme && `${species.baseSpecies}-${input.forme}`;
  if (cosmetic && species.cosmeticFormes?.includes(cosmetic)) species = dex.species.get(cosmetic);
  const moves = dex.moves.all();
  return {
    name: String(input.name ?? species.name).replace(/[|\r\n]/g, '').slice(0, 18) || species.name,
    species: species.name,
    item: input.item ? (byNum(dex.items.all(), input.item)?.name ?? '') : '',
    ability: input.ability ? (byNum(dex.abilities.all(), input.ability)?.name ?? '') : '',
    moves: (input.moves ?? []).map(n => byNum(moves, n)?.id).filter(Boolean),
    ppUps: input.ppUps,
    nature: input.nature,
    gender: input.gender ?? '',
    shiny: !!input.shiny,
    level: input.level,
    happiness: input.happiness,
    ivs: input.ivs,
    evs: input.evs,
  };
}

const choice = /^(p1|p2) (move [1-4]|switch [1-6])$/;
const end = /^(forcelose (p1|p2)|forcetie)$/;

const stream = new BattleStreams.BattleStream();
let started = false;

(async () => {
  for await (const chunk of stream) process.stdout.write(JSON.stringify(chunk) + '\n');
})();

readline.createInterface({input: process.stdin}).on('line', line => {
  try {
    if (!started) {
      const setup = JSON.parse(line);
      const dex = Dex.forFormat(setup.format);
      started = true;
      stream.write(`>start ${JSON.stringify({formatid: setup.format, seed: setup.seed})}`);
      for (const side of ['p1', 'p2']) {
        const team = setup[side].team.map(p => toSet(dex, p));
        stream.write(`>player ${side} ${JSON.stringify({name: setup[side].name, team})}`);
      }
    } else if (choice.test(line) || end.test(line)) {
      stream.write(`>${line}`);
    } else {
      process.stderr.write(`commande refusée : ${line}\n`);
    }
  } catch (e) {
    process.stderr.write(`erreur : ${e.message}\n`);
  }
}).on('close', () => process.exit(0));
