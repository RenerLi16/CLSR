# CLSR — Game Design Problem & Redesign Proposal

## 1. Core Objective

CLSR is a cooperative game designed to study **how AI influences group discussion, disagreement, and decision-making**.

The game needs to:

1. Give three players a shared problem to solve.
2. Create opportunities for **reasonable disagreement**, where multiple strategies have legitimate advantages.
3. Encourage players to explain, challenge, and reconsider their decisions.
4. Be engaging, intuitive, and simple enough for teenagers to play.

**The game does not need to be extremely complex or difficult. It needs to create meaningful decisions worth discussing.**

## 2. Problems With the Current Design

| Problem | Explanation |
|---|---|
| **1. Insufficient information** | Players cannot reasonably determine where to send expeditions or how many resources to allocate. Decisions resemble guessing rather than reasoning. |
| **2. Dominant strategies** | Sending maximum manpower toward distant leaf nodes is often better than alternatives. Supply locations also become obvious targets. |
| **3. Weak decision-making** | Players lack meaningful tradeoffs. Disagreements may come from different guesses rather than different, defensible reasoning. |
| **4. Limited interaction** | The game mostly involves selecting destinations and manpower, making it difficult to sustain engagement. |
| **5. Visual design conflict** | A large, realistic map requires zooming out, making locations difficult to inspect and interactions less satisfying. |

### Underlying Issue

**The current game creates uncertainty without giving players sufficient control over, or ways to reason about, that uncertainty.**

Adding more mechanics, resources, or restrictions will not necessarily solve this.

## 3. Proposed Redesign Principles

**A. Design meaningful decisions first.**

Each scenario should offer at least two reasonable strategies with competing advantages—for example, securing more immediate rescues versus investing resources to enable additional rescues later.

**B. Make decisions informed, not obvious.**

Players should receive enough information to estimate the consequences of their choices. Some uncertainty can remain, but it should not determine everything.

**C. Eliminate dominant strategies.**

The choice of destination, manpower, and expedition structure should all matter. No single strategy should consistently outperform the others.

**D. Introduce mechanics only when they improve decision-making.**

Potential mechanics include specialized teams, reconnaissance, resource allocation, and environmental changes. None should be added simply to increase difficulty.

**E. Prioritize meaningful interaction over photorealism.**

Use an understandable overview map with detailed location inspection, clear planning tools, and visible consequences of player actions.

## 4. Recommended Development Plan

1. **Start with a small paper prototype.** Ignore graphics and use a simple map with a few locations.
2. **Design one meaningful dilemma.** Create two or more defensible strategies based on information available to every player.
3. **Test the game loop.** Simulate all three days and check whether decisions remain interesting.
4. **Identify dominant strategies.** Test whether always sending maximum manpower, targeting leaf nodes, or prioritizing supplies consistently wins.
5. **Test group discussion.** Determine whether different players can reasonably advocate different plans and improve their decisions through discussion.
6. **Only then develop additional mechanics and the UI.**

Any substantive changes to the research gameplay should be reviewed through the appropriate study-approval process before participant testing.

## 5. Final Design Direction

The goal should not be to make CLSR a harder rescue simulation.

**The goal is to create a small, engaging cooperative strategy game where players have enough information to reason, enough uncertainty to disagree, and enough control for their decisions to matter.**

The key question for every mechanic should be:

*Does this create a meaningful decision that players can reasonably discuss, or does it merely make the game more complicated?*
