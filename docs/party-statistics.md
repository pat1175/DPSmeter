# Party statistics and resumable meter checkpoints

This proposal adds support, kills/deaths and healing to the existing damage meter.
It remains display-only (`affects_gameplay: false`); it does not change gameplay,
network messages, matchmaking, saves or progression.

## Controls and accounting

- **Support:** block made, ally block, ally buff additions, enemy debuff additions,
  and Skill plays that provided tracked ally block, buffs or healing. Hover rows
  for buff stacks by type. Provider block credit requires a card source; unknown
  passive sources still count as block received. Block granted is not damage prevented.
- **Kills / Deaths:** final-hit enemy kills, boss kills, player deaths and the last
  named boss finished. Hover for all boss finishes in the selected scope. Boss kills
  also count as enemy kills. Primary enemies in boss encounters earn boss credit;
  automatic minion cleanup does not. Prevented deaths are excluded, and a revived
  enemy can be credited on a later death.
- **Deaths:** confirmed player deaths once per life. Prevented deaths, summon deaths
  and abandoning a run are excluded. Revival allows another actual death to count.
- **Healing:** actual HP restored, healing given to other players, and healing received.
  Overhealing is excluded. Provider credit requires an identifiable executing model
  in the game's choice context; otherwise only the recipient is credited. Rest-site
  healing counts as received; direct HP assignment/revival does not count as healing.
- **Whole run / Combat:** selects the scope for support, cards, received damage,
  kills/deaths, healing and the copied report. Meter retains both damage scopes.
- **Copy summary:** copies all party contributions locally for manual sharing.
  No message is sent automatically.

Energy, draw, cleanses and damage amplification are not attributed. These are event
counts, not a universal contribution score. Summon tanking is not separately reported.
This proposal retains upstream's outgoing damage definition; counting enemy block
is proposed independently.

## Run lifecycle

Returning to the main menu clears and hides the visible meter. Each run has a local
checkpoint in `user://party_meter_runs`, identified by profile, seed, mode, player IDs
and start time. Solo and co-op archives coexist. Loading a saved co-op run can accept
one unambiguous start-time match within 120 seconds because guests initially use
their own clock while loading uses the host's saved start time. Ambiguous matches
are rejected. These local meter files are not synchronized through Steam Cloud.

Combat checkpoints retain both completed totals and the pre-combat baseline. If the
game replays an interrupted or saved fight, its old contribution is rolled back before
counting the fight again. Old seed-only state can be imported once for a matching
party and a run that predates that checkpoint. Earlier unobserved events cannot be
reconstructed.

Completed runs are archived as completed and are never restored as active sessions.
The report remains visible on the game-over screen until the menu opens. A text report
is also saved to `user://party_meter_last_run.txt`.

## Validation and remaining work

The earlier support build was tested in single-player and as a co-op guest with an
unmodded host on Windows game v0.107.1. Joining succeeded and incoming/blocked/HP-loss
statistics were observed. The new death, kill, healing and resume features still need
live gameplay verification. The isolated checks exercise accounting, duplicate death
notifications, revival, healing amounts, archive identity, disk reload, replay rollback
and Harmony bindings; they do not simulate gameplay or Godot rendering.

Queued display updates are coalesced and paint the latest service snapshot; tab changes
also read current state. Shutdown releases the overlay and cached references. A shutdown
hang was observed in the earlier local build, but its cause is unproven and this cleanup
is not a verified fix.

Upstream's target framework and release version are retained for review. The local live
build used .NET 9 to match the installed game runtime. Maintainers should select the
release target framework for the game builds they support; this proposal does not
publish binaries or alter the release pipeline.
