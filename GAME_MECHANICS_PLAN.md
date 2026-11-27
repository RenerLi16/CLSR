# Cascade Lab game mechanics implementation plan

This earlier plan describes the threshold-rule maps. The revised formal maps and their current balance results are documented in the [telescope map review](Tools/MapBalance/README.md).

Both formal maps now give the team public estimates, let it form and dispatch a plan, and present a correction before Day 2. The remaining work aligns both AI conditions with that flow and makes decisions and scores available for analysis.

## Current implementation

| Area | Blackridge | Cedar Valley |
| --- | --- | --- |
| Scenario data | Four branches, 18 survivors across 12 occupied sites, one +1 LP cache | Four branches, 15 survivors across 12 occupied sites, +3 and +1 LP caches |
| Between-day report | Day 1 correction updates North Pass 5 → 1 and South Spur 1 → 4 after acknowledgment | Day 1 correction updates River Road 5 → 1 and Cliff Pass 1 → 4 after acknowledgment |
| Play-test flow | Report screen follows the Day 1 outcome and private ratings. The next dispatch is gated until acknowledgment. | Same report flow |
| AI preview | The board uses current estimates. The backend receives original estimates and released report text from the shared public view. | Same preview path |
| Study data | Optional local event log captures phases, allocations, outcomes, and intervention text. Private answers are temporary. | Same preview path |

### 1. Cedar Valley scenario migration

Cedar Valley’s scenario asset now uses the instructor’s site and LP values and binds the Day 1 correction to the River Road and Cliff Pass roots. The starting estimates are North Ridge 2, West Woods 3, River Road 5, and Cliff Pass 1. C3 requires 3 LP throughout play. The validation targets are 11 of 15 for Cedar Valley and 11 of 18 for Blackridge.

### 2. Make the dispatch and day boundaries explicit

1. Keep `Core/RescueSimulation.cs` authoritative for the day, shared pool, dispatch validation, discoveries, one-time rescues, and one-time cache claims. Route all Unity dispatches through `RescueGameController` and this simulation. The formal scenarios start with 9 LP, allow at most 4 LP and two distinct destinations per day, and run for three days. The practice scene continues to read its own limits from its asset.
2. Validate a copied plan before committing it. Require a positive allocation on each leg, a known non-Camp destination, a valid Camp path, and a sum within both the daily cap and the available pool. An empty plan is a legal explicit decision. Lock the final plan once, retain it through the private pre-outcome forms, and spend it once on resolution.
3. Resolve each leg from Camp in parent order. Test every threshold against the leg’s allocation. Rescue all survivors at each passed site once, record the first blocking site, and stop there. Separate attempts along shared trails may reveal or pass the same site, but a saved survivor and a claimed cache can only contribute once.
4. Record spent LP, rescued people, newly discovered sites, the blocker, and pending cache LP per leg. Apply pending LP once before the following day’s planning. With 2 LP sent to Creek Shelter and 2 to Lakeside Camp on Blackridge Day 1, the team should rescue four people and start Day 2 with 6 LP. A 3-LP Fire Lookout dispatch and a 3-LP Gravel Pit dispatch on the remaining days should finish at 11.
5. Separate the completed round presented on the outcome screen from the next day shown on planning screens. Audit Tactical HUD and legacy labels for early day increments or misleading point balances. Prevent a double-click, timeout, stale draft, or duplicate callback from resolving a completed round again.
6. Test blocked and partially successful paths, simultaneous legs with a shared prefix, a repeat visit, a cache claimed on the last day, an exhausted pool, an empty day, and restart. Exercise the planner through the regular study flow as well as the engine entry point.

### 3. Present public estimates and discoveries consistently

1. Keep the initial map data in `Core/ScenarioSpec.cs` and retain the current public estimates, pending reports, and released history in `RescueSimulation`. Audit scenario validation for nonnegative counts, distinct roots within one report, and a release day earlier than the final game day. Retain legacy difficulty-report handling for any older scenario that still uses it.
2. Keep the sequence visible to all three conditions. Resolve the Day 1 expeditions, show the result, finish the private post-outcome ratings, show the report with the old and revised estimates, then update the board on acknowledgment. Day 2 observation and private judgments open afterward. Block dispatch while a report is pending and make repeated acknowledgment harmless.
3. Use the same estimate projection for branch signs, Observe, the HUD, and the AI request. Show the exact number rescued throughout play. Keep the true scenario denominator for the final outcome and the research export. If an estimated total is shown during play, label it as estimated and document how already-rescued people are incorporated.
4. Keep estimates and confirmed discoveries distinct. A blocked team learns the blocking threshold. A team that passes learns the site’s result and the allowed downstream counts. Preserve the source of each figure so a lower-confidence branch estimate cannot overwrite a confirmed local observation.
5. Prevent designer truth, hidden thresholds, unreached survivor locations, and future report text from appearing in player surfaces or AI payloads. Test both maps from a clean start, immediately before and after acknowledgment, during Day 3, at the final score, and after restarting. A new session must restore all estimates, claims, discoveries, selections, and report history.

### 4. Standardize the study round and intervention

1. Use the same public snapshot and round schedule for all three conditions. Capture three private judgments, the provisional team plan, the intervention, further discussion, and the locked final plan before resolving the day. Keep condition assignment fixed within the session and record the scenario order.
2. In `ReviewSupportSnapshot`, add a structured estimate field with the initial value, current value, availability day, and revision status for each root. Add released report IDs and the public old/new pairs. The current preview uses `rescue-support-v2` with strict validation. Coordinate a versioned update in Unity, `backend/support.py`, and its provider and tests so both ends use the same schema.
3. Freeze a snapshot when the provisional plan is locked. Include valid public destinations, confirmed discovery data, legal LP constraints, remaining days, and the team’s actual allocation. Keep private free text and hidden scenario truth outside the request. Reject stale replies from a different session, scenario, or round.
4. Implement the instructor’s proposed reproducible behavior as a deterministic policy with scenario-independent rules. Constructive dissent should name an assumption in the actual provisional allocation. On Day 1 it can question relying heavily on a preliminary count. After a correction it can ask what supports continuing to prioritize the downgraded branch or allocating too little to test the revised lead. If the plan already responds to the correction, test a genuine uncertainty in that plan.
5. Direct recommendation should produce a legal, useful plan from the same public snapshot. Generate feasible options within the pool and daily cap, use known thresholds and confirmed discoveries when available, rank options using current public estimates and information value, and explain limits where access is unknown. An empty provisional plan still receives a defensible recommendation when LP are available.
6. Keep a matched neutral pause for No AI and send no intervention request in that condition. Give each condition the same reading and discussion time. Start timing on display, not on request. Log the status and exact text shown even when support times out. Preserve the existing mock path for editor and offline QA. If the study uses live generation instead of the instructor’s deterministic policy, pin that choice before pilots and track provider and prompt versions.

### 5. Capture decisions and scores

1. Create a per-session record before formal play. Store a pseudonymous session ID, scenario IDs and revisions, scenario order, condition, and policy version. Keep the practice round separately identifiable.
2. Save each private judgment before clearing the screen for the next player. Store player slot, branch choice, allocation, reason, confidence, and explanation. Capture private pre- and post-outcome ratings and scenario-end responses with their phase and question IDs. Distinguish skipped answers from submitted text.
3. Save the provisional and final assignments as immutable records. Capture the public snapshot, exactly what intervention was shown, its provider or fallback status, report shown and acknowledged events, and the result of each leg. Persist the day record at phase boundaries with stable keys so a retry cannot duplicate a round or erase a submitted form.
4. Make the handoff screen blank of previous answers, destination selections, and draft routes. Require the next player to open their own form. Keep a shared-screen instruction for the other players to look away. Keep export and any facilitator-only truth view behind a separate control from participant play.
5. Export the exact final rescued count as the primary game outcome, alongside the scenario’s total and day-by-day totals. Compute whether teams changed branches or LP allocations from the saved plans. Use the private reasoning fields to distinguish noticing or weighing the correction from simply switching destinations. Preserve both incomplete and completed sessions for QA with a completion flag.

### 6. Verify before a pilot

| Check | Required evidence |
| --- | --- |
| All nine instructor checks | Export the shipped assets and confirm 9/9 for each formal map. Blackridge’s benchmark is 11/18 optimal, 4 rescues of opening spread, 63 near-optimal openings, 7 vs. 11 for the board heuristic, two rescues lost when any day is skipped, no route above 4 LP, 12 occupied sites versus six dispatches, a branch shift toward South Spur, and 5 → 7 with the revision. |
| Simulator and Unity agree | Replay identical plans in both for a blocked route, a multi-survivor site, a shared prefix, a cache reward, and the known 11-rescue Blackridge route. Compare LP, discoveries, and scores after each round. |
| Public information stays public | Test Day 1, the report boundary, Day 2, final reveal, and restart. Assert that a pure estimate correction cannot change a route’s threshold or its solver-optimal score. |
| AI behaviors differ as intended | Replay an unchanged provisional plan across the three conditions on both sides of the correction. Verify a legal direct recommendation, a specific dissent prompt, a neutral bypass, and equal pacing. |
| Research records reconcile | Export a synthetic session with missing answers and a support timeout. Confirm the saved plans, notes, scores, and acknowledgment events match what participants saw. |
| Scene is playable | On both maps, verify every node and trail in the Unity planner at 1280×720 and 960×540. Test map fit, zoom, drag, hover cards, overlapping routes, cache visibility, and advancing from the report screen to Day 2. |

Use the instructor’s existing verifier for acceptance. Its board heuristic has access to hidden scenario data when choosing a destination. Run the independent public-information policies in `Tools/CLSR.Offline` before drawing conclusions about how the deployed AI will behave. Freeze the two map revisions, report timings, intervention policy, and export schema before collecting participant data.
