# BusJam.Core — Clean Bus-Jam Architecture

A SOLID, event-driven, ScriptableObject-data-driven implementation of the classic
"Bus Jam" loop (tap a free passenger → it boards a matching bus or waits in a slot).

This lives in namespace `BusJam.Core` and is **fully independent** of the existing
`BusJam` game in this project — nothing here touches those files.

## Files

| Layer        | File                | Responsibility |
|--------------|---------------------|----------------|
| Data         | `ColorType.cs`      | Color enum (None = unset). |
|              | `ColorPalette.cs`   | ColorType → Unity Color (single source of truth). |
|              | `Data/PassengerData.cs` | SO: passenger speed / feel. |
|              | `Data/BusData.cs`   | SO: bus color, capacity, drive timings. |
|              | `Data/LevelConfig.cs` | SO: grid layout, slot count, bus sequence. |
| Comms        | `GameEvents.cs`     | Static type-safe event bus. |
| Actors       | `Passenger.cs`      | State machine + movement (coroutine tween). |
|              | `Bus.cs`            | Seat bookkeeping with reservation pattern. |
|              | `GridCoord.cs`      | Immutable (row,col) struct. |
| Managers     | `GridManager.cs`    | Occupancy + BFS "is this passenger free?". |
|              | `BusManager.cs`     | Bus queue, active stop, boarding, traffic. |
|              | `SlotManager.cs`    | Holding area + auto-flush on bus change. |
|              | `InputManager.cs`   | Tap detect → validate → route. |
| Orchestrator | `GameController.cs`  | Level build, win/lose arbitration. |

## Scene setup (10 minutes)

1. **Create data assets** (Project window ▸ right-click ▸ Create ▸ BusJam):
   - One `PassengerData`.
   - A few `BusData` (e.g. Red×3, Blue×3, …).
   - One `LevelConfig`: fill the `grid` rows (row 0 = front, nearest the bus),
     set `slotCount`, and drag your `BusData` into `busSequence` (arrival order).

2. **Scene anchors** (empty GameObjects):
   - `GridOrigin` — front-left of the grid.
   - `StopPoint`, `ArriveFromPoint` (off-screen +Z), `DepartToPoint` (off-screen −Z).
   - `Slot 0..N` — one Transform per holding slot, left to right.

3. **Managers** — add the five manager components (anywhere; one GameObject is fine)
   and drag references in the inspector:
   - `GridManager.gridOrigin` → GridOrigin.
   - `BusManager` → stop / arriveFrom / departTo points (+ optional bus prefab).
   - `SlotManager.slotPoints` → the slot Transforms; `.busManager` → BusManager.
   - `InputManager` → camera + the three managers; set `passengerMask` to your
     passenger layer.
   - `GameController` → all four managers + the `LevelConfig` + `PassengerData`
     (+ optional passenger prefab).

4. **Prefabs are optional.** If `busPrefab` / `passengerPrefab` are left null, the
   managers generate primitive placeholders (capsule passengers, cube buses) so you
   can play-test immediately. Passenger prefab must have a Collider for raycasting.

5. Press Play. Subscribe your UI to `GameEvents.LevelCompleted` / `GameEvents.GameOver`.

## Notes

- **DOTween is not installed.** Movement uses a dependency-free eased coroutine in
  `Passenger.MoveRoutine`. To switch to DOTween, replace that one method body — the
  rest of the codebase is unaffected (see the comment block there).
- **Deadlock tuning** lives in `GameController.EvaluateDeadlock()` — the single place
  to add forgiveness (jokers, extra slots, etc.).
- **No LINQ on hot paths, zero per-query allocation** in `GridManager` (BFS buffers
  are pre-allocated and reused).
