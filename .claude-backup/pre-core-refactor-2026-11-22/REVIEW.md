# Cascade Lab: Search and Rescue — review beta

This is a gameplay and research-procedure preview, not the final participant build.
The visuals, scenario layouts and costs are provisional. The main project checkout
is separate from this review branch.

## Play in Unity

Open this folder with Unity 6000.5.4f1, open the **CedarValley** scene and press **Play**.
Choose a condition or leave **Random** selected. For a quick review, check **Skip
countdowns for reviewer walkthrough**. Otherwise the intervention phases retain the
60-second initial discussion, 15-second reading interval and 45-second further discussion.

Click destination nodes or use the list in the left panel. Three people pass the
shared screen for private forms. A reviewer can also step through alone with fictional
or blank answers. There is one unscored practice dispatch and then two three-round
draft scenarios, Cedar Valley and Blackridge, in either order.

## What this preview demonstrates

- Each formal scenario starts with nine logistics points. Up to four points and
  two expeditions can be allocated per simulated day. Used points are spent,
  even on a blocked expedition; unused points carry over.
- An expedition follows one path from base camp. Every node it can reach is
  searched; if it is blocked, it sees the blocking threshold only. Each occupied
  site holds one survivor. Reaching a junction reveals separate totals for each
  immediately connected child branch; unreached sites still hide exact survivor
  locations and difficulty unless the expedition was blocked there. Cedar Valley demonstrates caches that add points on
  the following day and a scripted report after Day 2.
- Daily flow: observe, three private initial judgments, initial discussion and
  provisional plan, one sample or neutral message, further discussion, final plan,
  three private pre-outcome ratings, dispatch results, and three private post-outcome
  questionnaires. Each scenario ends with four private reasoning prompts per player.
- The questions follow the submitted Participant Materials, with terminology
  updated from manpower to logistics points. Registration, consent and eligibility
  items are outside the game.

The two AI conditions use labeled local examples by default. In the Unity Editor
or a native development build, enable **Use local mock backend** in Review setup
to test the local connection. Start the backend first (from the project folder):

```sh
python3 -B -m backend.server --port 8787
```

Lock the provisional plan to send one request. The 15-second reading timer starts
when the mock message or unavailable notice appears. No AI uses a local neutral
message and sends nothing. The web preview still uses offline examples only.

Use fictional answers. The preview does not call a live model, save responses,
or send them over the internet. It has no data export, consent form, login, online
rooms, or storage integration. This is suitable for reviewing the flow, not for
running or collecting study data.
