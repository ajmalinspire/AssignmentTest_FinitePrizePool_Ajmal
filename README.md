
# A Note on How I Built This: AI-Assisted and Agentic Development

I used AI heavily on this assignment, and I want to be upfront about it. I used [tools, e.g. Claude, Claude Code, Unity CLI] to explore the problem, plan the architecture, generate code, write tests and review my own work.

**Why this task suits AI-assisted development**

This assignment is deliberately broad. It asks for:

exact integer-cent math and a deal-size calculator across 40 mode/bet combinations
a cryptographically shuffled deal builder with placement constraints and retry limits
crash-safe saving, pending-result recovery and a deal lifecycle (Ready, Active, Completed, Retired)
a background-built successor deal, a retire/audit flow and a Unity client with debug tools
Edit Mode tests, a simulation report and a design note

Even though the estimate in brief is 4–5 working days, this test assignment was completed in 2 days deadline given by HR.I respected the deadline as I was confident in building this with AI Assistance, and also needed a case where I could apply and learn more Agentic Developement.
Much of that work required for this task is well-defined and repetitive: boilerplate, serialization, assembly definitions, test scaffolding, CSV export, editor windows. AI is very good at that. It let me spend my time on the parts that need judgment.  
-getting the math exactly right  
-designing the engine/game boundary (IDealService) so the engine could move to a server  
-deciding how to handle failure cases: crashes mid-save, mid-animation exits, impossible configs  

**How I worked**  

I read the specification and understood the model first. Finite pools, bags, cycles, paired cycles, RTP and placement limits. I did not delegate understanding.
I used AI to plan and build in small steps. Engine first, then calculator, builder, spin service, persistence, lifecycle, UI.
I treated AI output as a draft, not an answer. Every piece was checked against the brief.
I verified with tests and simulation rather than trust. The Edit Mode tests cover the §8.1 scenarios, and the simulation report shows actual RTP across complete deals. Exactly 90% RTP is a property that can be proven in code, and that is the check I relied on.
I documented what I skipped or assumed, as the brief asks, in the design note.

**What is mine:** the architecture decisions, the interpretation of ambiguous requirements, the review and correction of generated code, and the acceptance criteria I tested against.

**What this shows about how I’d work on your team**  
Speed with rigor. Work that would take days to type by hand can be built, tested and iterated much faster, and I use the saved time on verification and edge cases.
Breadth. Math, concurrency, persistence, tooling and UI in one project. AI lets one engineer cover that range without cutting corners.

**A perspective on the changing landscape:**

Software development is shifting quickly. Writing every line by hand is becoming less of the job, and designing, specifying, verifying and shipping correct systems is becoming more of it. I believe a developer should be judged on the quality of the solution: whether it is correct, well structured, tested, explainable and maintainable. Typing speed or the manual authorship of each line should count for much less. Compilers, IDEs, libraries and Stack Overflow each changed what “writing code” meant, and AI is the next step in that progression.

I’m glad to walk through any part of the code, explain the reasoning behind a decision, or modify it live. I stand behind every part of this submission, and I’d love to be evaluated on whether it works and whether I understand it.

# Finite Prize Pool (Unity)

A finite-prize-pool game built entirely in Unity and C#. Prizes are not drawn at random on each spin: they are
built in advance into a **deal**, a fixed, shuffled list of Regular prizes, Big prizes and losses. Every spin
takes the next ticket. When a deal is fully used, its RTP is exactly 90%.

**Unity version: 6000.3.9f1 (Unity 6.3 LTS).** The Unity project is in `FinitePrizePoolProject/`.

> **Assumed bags.** The Inner and Outer bags come from the *Finite Pool Mathematics* PDF (pages 2-3), which was
> not available. I have contatcted hr , and she said this brief is the only pdf available. The config uses made-up bags (`assumed-bags-v1`) chosen so that every fact the brief states
> about them holds. See `Docs/DesignNote.md`, "Assumptions". Swapping in the real bags means editing one JSON file.

## Open and run

1. Open `FinitePrizePoolProject/` in Unity 6000.3.9f1. The packages it needs are in `Packages/manifest.json`:
   `com.unity.nuget.newtonsoft-json` (JSON saves), `com.unity.test-framework` (tests) and `com.unity.ugui`
   (Canvas and TextMeshPro).
2. Open `Assets/FinitePrizePool/Scenes/Game.unity`. If it is missing, use
   **Tools > Finite Prize Pool > Build Canvas UI**.
3. Press **Play**. Pick a player, a mode and a bet, then press **SPIN** (or Space).

The game screen is a Unity Canvas (uGUI + TextMeshPro) laid out in `CanvasUI/Prefabs/GameCanvas.prefab`, and
restyled in the Editor like any other UI. It calls the engine only through `IDealService`.

The developer debug panel (bottom left; F1 hides it) shows the deal ID, state, config version and spins
played. It is compiled only into the Editor and development builds.

## Editor tools (menu **Tools > Finite Prize Pool**)

| Tool | What it does |
|---|---|
| Deal Size Table | All 40 mode/bet combinations from the current config; **Export CSV** |
| Deal Admin | Retire an active deal (Play mode), apply a changed config, view completed and retired deals and the retire log, export audit CSVs |
| Simulator | Plays whole deals quickly through the real engine, without animation; writes the simulation report. **Real-world play** runs every test player in sessions on random modes and bets, sharing deals, until *n* deals are completed; it reports each player's RTP, every completed deal (exactly 90%) and the deals left unfinished |
| Build Canvas UI | Rebuilds the Canvas prefabs and the game scene from scratch (imports TMP Essentials if missing; run it twice the first time) |
| Write Reports | Writes `Docs/DealSizeTable.csv` and `Docs/SimulationReport.md` without a dialog (also from the Unity CLI) |

## Tests

**Window > General > Test Runner > EditMode > Run All.** 47 tests; they cover every scenario in brief §8.1
(the test names say which), plus tampering, crash-safe saving and the "no `System.Random`" rule.

From the command line (Unity CLI): `unity test FinitePrizePoolProject --mode EditMode --output test-results.xml`.

## Structure

| Assembly | Folder | Contents |
|---|---|---|
| `FinitePrizePool.Engine` | `Assets/FinitePrizePool/Engine` | The deal engine: plain C#, `noEngineReferences`, no MonoBehaviour. Config, maths, placement, builder, verifier, saving, spin service, lifecycle, reports |
| `FinitePrizePool.Game` | `Assets/FinitePrizePool/Game` | `GameRoot` (creates the engine), `SpinPresenter` (reveal timing), `PrizeText`, Unity adapters |
| `FinitePrizePool.CanvasUI` | `Assets/FinitePrizePool/CanvasUI` | The Canvas game screen (`CanvasSpinScreen`) and its prefabs |
| `FinitePrizePool.CanvasUI.Editor` | `Assets/FinitePrizePool/CanvasUI/Editor` | The Build Canvas UI tool |
| `FinitePrizePool.Editor` | `Assets/FinitePrizePool/Editor` | Editor windows |
| `FinitePrizePool.Tests` | `Assets/FinitePrizePool/Tests` | Edit Mode tests (NUnit) |

Inside the engine:

| Folder | Responsibility |
|---|---|
| `Config` | Config JSON, validation, bags. Every config has a `configVersion` |
| `Maths` | `DealSizeCalculator`: exact deal sizes in integer cents |
| `Placement` | §4.4 limits, the impossibility check, `PlacementValidator`, and two strategies |
| `Building` | `DealBuilder` (§4.3 steps 1-3) and `DealVerifier` (step 5) |
| `Persistence` | `IPoolStore`, `JsonFileStore` (atomic writes), `InMemoryStore` |
| `Service` | `DealService`: spins, the Ready/Active/Completed/Retired lifecycle, background successors, retire |
| `Reports` | Deal-size table, audit CSVs, simulator |

## Config

`Assets/FinitePrizePool/Resources/FinitePrizePoolConfig.json` holds the bags, bet levels, target RTP (basis
points), bonus unit value, placement limits and generation attempts. Change `configVersion` whenever you change
anything. A running game picks it up through **Deal Admin > Apply config file**: active deals keep their
version, and Ready successors are rebuilt.

## Saves

`Application.persistentDataPath/FinitePrizePool/` (Deal Admin > Open save folder):

| File | Contents | Written |
|---|---|---|
| `ledger.json` | balances, deal positions, the unshown spin | atomically, every spin |
| `history.json` | completed and retired deals, the retire log | atomically, when a deal ends |
| `deals/<id>.json` | one deal's full ordered ticket list | once, before first use; never changed |
| `spins.jsonl` | every spin record | appended after the ledger |

Each write goes to a temporary file first and is swapped in with `File.Replace`, which keeps a `.bak`.

## Documents

| File | |
|---|---|
| `Docs/DealSizeTable.csv` | The deal-size table for all 40 combinations (§7.2) |
| `Docs/SimulationReport.md` | 10 complete Inner $1.00 deals and one Outer $1.00 deal (§7.4) |
| `Docs/PlacementComparison.md` | Success rate of the literal "shuffle and check" against the constrained shuffle |
| `Docs/DesignNote.md` | Answers to §8.2, decisions, what was assumed or skipped (§7.6) |
