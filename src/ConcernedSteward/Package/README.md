# Concerned Steward

Sunniva keeps your settlement's fires burning.

She does one job — tending the fuel-burning lights inside the settlement you
marked, with fuel from the chest you marked — and she does that job honestly
rather than doing many jobs approximately.

**Requires Concerned NPC** (`TheConcernedCat-ConcernedNPC`), the shared runtime
her identity and job planning come from. Without it the mod reports the missing
dependency at load and does nothing.

## Who she is

A warrior-priestess who fell in battle, whose calling was carrying light into
dark places. The first time you pick up resin with her story unstarted, you also
find a flint and steel with runes carved into the metal. There is one of it for
the life of that world; it is a discovery the mod remembers, never an item put
into your inventory, and your resin pickup is untouched. Read the runes, mark a
settlement, and she turns up. Recruit her and she moves in.

## What she actually does

1. You mark a settlement area and one supply chest.
2. You recruit her, standing inside the settlement.
3. She looks at every fuel-burning light inside the area (anything built on
   vanilla's fireplace component, such as campfires, hearths and standing
   torches; decorative lights with no fuel are never touched), works
   out which are closest to going out and how much the whole round needs, and
   walks to your chest **once** for all of them.
4. She walks a circuit of the lights. At each one she checks it again — if you
   filled it yourself, or it was destroyed, she moves on — and puts real fuel
   in, one piece at a time, through the game's own "use item on fire" path.
5. Whatever she did not use, she walks back and puts in the chest.

She walks the whole way. She is never teleported, never shoved, and her
position is never written by this mod.

## What she will not do

- **She will not create fuel.** Every piece she puts on a fire came out of your
  chest first, and that was measured, not assumed. If the lights need resin and
  the chest has none, she tells you what she needs instead of tending anything
  with nothing.
- **She will not take from, or put into, any chest but the one you marked.**
- **She will not work outside the area you marked**, and she will not touch a
  light under a ward that is not yours — she tells you it is burning low instead.
- **She will not touch a fire or a chest that this game session does not own.**
- **She will not take ownership of anything**, claim a bed, block a doorway,
  fight, carry loot, or open a door.
- **She will not run in multiplayer yet.** She works when you are the host, not
  on a dedicated server, and with nobody else connected. Otherwise she stops and
  says so.

## Turning her on

She is **off by default**. In `com.theconcernedcat.valheim.concernedsteward.cfg`:

```
[Steward] StewardRuntimeEnabled = true   # she exists
[Steward] TendFiresEnabled = true        # she may reach into your chest
```

Installing the mod changes nothing until you do that.

Then, in the console (F5):

```
cs_steward area 32       mark a settlement area, 32 m around you
cs_steward depot         mark the chest you are looking at as the supply chest
cs_steward runes         look at the flint and steel properly
cs_steward recruit       take her on (stand inside the settlement)
cs_steward status        what she is doing, and why she is not doing anything
cs_steward resolve       after she stops and asks you to look
cs_steward dismiss       let her go; her body stays and nothing is destroyed
cs_fires                 every light she looked at, and her verdict on each
```

`cs_steward status` always explains itself.

## Settings worth knowing

| Setting | Default | What it does |
|---|---|---|
| `[Upkeep] RefuelBelowSeconds` | 300 | A light with less burning time left than this is due. |
| `[Upkeep] RefuelToSeconds` | 1200 | What a tended light is topped up to (never past its capacity). |
| `[Upkeep] MostUnitsPerLight` | 10 | The most one light gets in one round. |
| `[Upkeep] CarryPerTrip` | 50 | How much fuel she takes in one trip (read at start-up). |
| `[Steward] SpeakOnScreen` | true | Show what she says on screen as well as in the log. |
| `[Appearance] Garment` | (empty) | A vanilla dress or robe prefab to try before the leather. |
| `[Appearance] HairStyle` | `$customization_hair12` | Her hair (vanilla's "Matronly"). |
| `[Appearance] ShowAtHome` | `true` | Show her at her home post as a player-style figure while she has no worker body. |

Thresholds are measured in burning time, not fuel, because pieces burn at
different rates. A threshold that would make her loop is refused and logged.

**One fuel kind per trip.** A settlement with both wood and resin lights gets
one trip for each kind, never one trip per light.

**Her look.** She wears a leather tunic and leather pants. Vanilla only draws
hair, hair colour, skin colour and the female model on a player-style body; on
the default base creature those settings may have no visible effect, and the
log says so (`[Appearance] HairColour` is her worker body's hair colour; the
figure uses its own fair, light-blonde colouring). At rest, with no worker body in the world, she is shown beside the
supply chest as a player-style figure that does show them: female, Matronly
hair, fair and light blonde. That figure is seen only on the host's machine and
changes nothing in the world; it is never there at the same time as her worker
body.

## Compatibility

Concerned Steward does not require, reference or interfere with Concerned
Cartographer, Concerned Teamster or Concerned Foreman. It needs Concerned NPC.
It makes no network calls of its own and patches nothing.

## Reporting a problem

Open an issue at
<https://github.com/Weakened/ConcernedCatMods>, and please include the output
of `cs_steward status`.
