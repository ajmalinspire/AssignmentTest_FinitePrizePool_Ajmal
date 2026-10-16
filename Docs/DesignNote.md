# Design note: Finite Prize Pool

| | |
|---|---|
| **Unity** | 6000.3.9f1 (6.3 LTS) |
| **Config** | `FinitePrizePoolConfig.json`, version `assumed-bags-v1` (the bags are assumed; see [Assumptions](#assumptions)) |
| **Sources** | `DealSizeTable.csv` (all 40 mode/bet sizes), `SimulationReport.md` (10 Inner + 1 Outer complete deals) |

## Answers to §8.2

### 1. Why does Inner fit in one paired cycle at $1.00, while Outer needs nine?

**Because $72.00 divides by $0.90 and $238.00 does not.** One cycle fits only when *P* is a multiple of 90 cents.

| $1.00 bet | *P* per paired cycle | *P* / $0.90 | Smallest *k* | Deal |
|---|---|---|---|---|
| Inner | $72.00 (60× multipliers + 12 bonus units) | 80 | 1 | 80 spins: 11 prizes, 69 losses |
| Outer | $238.00 (238 = 2·7·17, no factor of 3) | 264.44… | 9 | 9 × $238 / $0.90 = 2,380 spins |

### 2. Why does Outer at $4.75 need such a large deal, and what follows?

**$4.75 is 19 quarters, and 19 is prime.** One Outer cycle pays $1,029.25 = 102,925 cents, which shares no factor with 0.9 × 475 = 9·19, so *k* must be 9 × 19 = **171 paired cycles: 41,170 spins, 3,078 prizes, $195,557.50 wagered**. Outer $4.25 (17 quarters) needs 153 for the same reason. With no timeout:

- **It may never finish.** At 10 spins a minute it is about 69 hours of play on one device. RTP is exact only at the end, so until then the deal holds back paid-in money or owes unclaimed big prizes, possibly for months.
- **It blocks change.** A config change applies only to the next deal, so this slot keeps the old version until it ends. Retiring is the only way out, and a retired deal never reaches its target.
- **It costs storage and time.** A 4.6 MB deal file (Inner $1.00: 10 KB) plus its Ready successor, each re-verified when it becomes active.
- **Mitigations:** tolerance mode (implemented: Outer $4.75 becomes one cycle, 241 spins, planned RTP 89.91%), a retire policy by age or idle time, or bet levels and bags chosen so that *P* / (0.9·*b*) needs few cycles.

### 3. Why must a ticket never be returned when the game closes mid-spin?

**The outcome is decided when the wager is charged, not when it is shown.** The ticket is taken, the award credited and the record saved before any animation; the animation only displays it. If closing returned the ticket, a player could quit on every loss and push losses onto the next spin, theirs or another player's. The accounting would break too: a ticket played twice, or never, means wagers and payouts no longer total exactly 90%. So the ticket is final, and on reopening the saved result is shown (§4.5; test 6).

### 4. Does a player who plays 50 spins get 90% RTP?

**No.** 90% is exact only over a *whole deal*, which every player on the device shares and which may already be partly used. 50 spins are a slice of it:

| 50-spin blocks in the simulation | Lowest | Highest |
|---|---|---|
| Inner $1.00 (10 deals) | 46% | 114% |
| Outer $1.00 (1 deal) | 6% | 316% |

Averaged over many players and slices it is 90%; any one player's 50 spins can land far from it.

### 5. Why is shuffling while building acceptable when spins must not be random?

**Randomness only sets the order, once, before play; it cannot change what the deal pays.** The deal is then fixed, saved and verified from the saved file (every total, every cycle, the exact 90%) before its first spin. The shuffle uses a cryptographic generator, so the order cannot be predicted. A random draw at spin time would make RTP only *converge* to 90%, and a past outcome could not be audited. Here every outcome traces to a ticket position in a saved deal.

### 6. What would change for a server shared by many devices?

**Only the implementations behind two interfaces.** The game sees only `IDealService` (`Spin`, `GetSlot`, `GetUnshownResult`, `MarkResultShown`), so the client needs a network implementation of it. On the server:

- `IPoolStore` becomes a database. A spin is one transaction: check balance, take the ticket at the deal's position (row lock or compare-and-set), credit, insert the record.
- Spins carry an idempotency key, so a retry after a timeout cannot take two tickets.
- Balances, the unshown-result record and authentication move server-side; the client is never trusted.
- Successor builds become worker jobs; config, audit and retire become an admin service.

## Decisions

| Topic | Decision and reason |
|---|---|
| Money | `long` cents throughout. "Exactly 90%" is the integer test payout × 10000 == wager × 9000; no float or double |
| Placement | Default **ConstrainedShuffle**: Fisher–Yates on the prize order, then each loss goes into a random gap that still has room under all three §4.4 limits. The literal §4.3 **ShuffleAndCheck** is kept and selectable, but succeeds in 0 of 200 attempts for every deal above 11 prizes; ConstrainedShuffle succeeds in 89–100% (`PlacementComparison.md`). Limits are never loosened: impossible ones are rejected before any attempt, otherwise at most 1,000 attempts, then reject and log. `PlacementValidator` checks every list |
| Saving | `ledger.json` holds everything that changes per spin, written once per spin, atomically (temp file, then `File.Replace` with a `.bak`). Deal files are written once and read back to verify |
| Successors | Built on a thread-pool task and registered under the service lock; re-verified when they become active |
| Client | A Unity Canvas screen (uGUI + TextMeshPro) in a prefab, restyled in the Editor. It calls only `IDealService`, and its debug panel is compiled out of release builds |

## Assumptions

**The bags are made up.** The *Finite Pool Mathematics* PDF (pages 2–3) was not available. These bags reproduce every fact the brief states: Inner $1.00 = 1 cycle, Outer $1.00 = 9, Outer $1.75 with one cycle is fractional, Outer $4.75 is very large, and Inner $1.00 with max consecutive losses 5 is impossible. With the real bags, only the JSON, the figures here and one marked test change.

| Mode | Regular bag (ticket count in brackets) | Big bag |
|---|---|---|
| Inner | 1× (3), 2× (2), 3×, 5×, bonus 2 | 20×, 25×, bonus 10 |
| Outer | 1× (4), 2× (3), 3× (2), 5× (2), 10×, bonus 2, bonus 5 | 25×, 50×, 100×, bonus 20 |

- A "gap" counts the spins strictly between two slots of a kind; the deal's start and end count as ends.
- "A fixed repeating interval" means the distances between consecutive prize slots repeat with any period (constant spacing is period 1). Such a deal is rejected and rebuilt.
- A spin is refused while the previous result has not been shown.
- A Ready successor discarded by a config change is recorded as Retired, with 0 tickets used.

## Skipped, and known limitations

**Skipped:** visual polish and reel animation (a short "Spinning…" delay only), and Play Mode tests (the engine has 47 Edit Mode tests and the client is thin).

| Limitation | Effect | Fix |
|---|---|---|
| Ledger backup | If `ledger.json` is unreadable, the one-spin-old `.bak` is loaded and that ticket is played again | Repair from the spin log |
| Deal IDs after a failed save | The rollback rewinds the deal counter, so a successor built meanwhile can have its ID reused | Check each deal file's ID, mode, bet and config version on load |

