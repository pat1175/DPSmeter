using System.Reflection;
using System.Text.Json;
using DPSMeter;
using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS " + message);
}

var stats = new SupportMetrics();
stats.RecordBlock(12, false);
stats.RecordBlock(7, true);
stats.RecordBlock(7, true);
stats.RecordBlock(7, true);
stats.RecordBlock(-5, true);
stats.RecordBlock(0, true);
Check(stats.BlockGiven == 33 && stats.AllyBlock == 21, "Self block excluded from ally totals; all three allies counted; nonpositive ignored");
stats.RecordBlock(2.9m, true);
Check(stats.AllyBlock == 23, "Fractional block follows integer game accounting");
stats.RecordBuff("Strength", 2);
stats.RecordBuff("Strength", 2);
stats.RecordBuff("Dexterity", 1);
stats.RecordBuff("Strength", -1);
Check(stats.AllyBuffs == 3 && stats.AllyBuffStacks["Strength"] == 4 && stats.AllyBuffStacks["Dexterity"] == 1,
    "Buff recipient events and per-type stacks kept separate; decay excluded");
stats.RecordCard("Skill", false, true);
stats.RecordCard("Skill", false, false);
stats.RecordCard("Power", false, true);
stats.RecordCard("Attack", true, false);
Check(stats.Cards == 4 && stats.Skills == 2 && stats.Powers == 1 && stats.Attacks == 1 && stats.AllySkillPlays == 1 && stats.Auto == 1,
    "Skill, Power, Attack, auto and observed ally-support counts remain distinct");
var copy = stats.Clone();
copy.RecordBuff("Strength", 10);
Check(stats.AllyBuffStacks["Strength"] == 4, "Overlay snapshots do not mutate live totals");
var saved = new SavedState { StableRunId = "test-run", Players = new() { new SavedPlayer { PlayerKey = 1, RunSupport = stats } } };
var restored = JsonSerializer.Deserialize<SavedState>(JsonSerializer.Serialize(saved))!;
Check(restored.Players[0].RunSupport.AllyBlock == 23 && restored.Players[0].RunSupport.AllyBuffStacks["Strength"] == 4,
    "Run support totals and buff detail survive JSON persistence");
var legacy = JsonSerializer.Deserialize<SavedState>("{\"StableRunId\":\"old\",\"Players\":[{\"PlayerKey\":1,\"TotalDamage\":42}]}")!;
Check(legacy.Players[0].RunSupport.Cards == 0, "Older meter saves load with zero support history");

var ledger = new KillCreditLedger<object>();
var deathLedger = new DeathLedger<object>();
var victim = new object();
Check(!deathLedger.Confirm(victim, true, true) && !deathLedger.Confirm(victim, false, false),
    "Prevented deaths and living players do not count as deaths");
Check(deathLedger.Confirm(victim, false, true) && !deathLedger.Confirm(victim, false, true),
    "One actual death counts once despite repeated notifications");
deathLedger.ObserveHealth(victim, false);
Check(!deathLedger.Confirm(victim, false, true), "A still-dead player cannot earn another death");
deathLedger.ObserveHealth(victim, true);
Check(deathLedger.Confirm(victim, false, true), "Revival allows the next actual death to count");
Check(SupportMetrics.EffectiveHealing(45, 50) == 5 && SupportMetrics.EffectiveHealing(50, 50) == 0 &&
    SupportMetrics.EffectiveHealing(50, 45) == 0, "Healing uses actual restored HP, excluding overhealing and damage");
stats.RecordHealing(5, true); stats.RecordHealing(8, false); stats.RecordHealing(-10, true);
stats.HealingReceived = 4; stats.Deaths = 2;
Check(stats.HealingDone == 13 && stats.AllyHealing == 5, "Self and ally healing totals remain distinct");
var enemy = new object();
Check(ledger.Confirm(enemy, false) == null, "Automatic enemy cleanup without a sourced lethal hit earns no kill");
ledger.Capture(enemy, 11, false, "Enemy");
Check(ledger.Confirm(enemy, true) == null && ledger.Confirm(enemy, false) == null,
    "Prevented death discards pending credit and cannot leak to a later unrelated death");
ledger.Capture(enemy, 22, true, "Boss A");
var credit = ledger.Confirm(enemy, false)!;
Check(credit.PlayerKey == 22 && credit.IsBoss && ledger.Confirm(enemy, false) == null,
    "Final hitter receives one boss credit even when death notification repeats");
ledger.Capture(enemy, 11, false, "Enemy revived");
Check(ledger.Confirm(enemy, false)?.PlayerKey == 11, "A revived enemy can credit its next actual killing hit");
ledger.Capture(enemy, 11, true, "Stale boss");
ledger.Clear();
Check(ledger.Confirm(enemy, false) == null, "Leaving combat discards unresolved kill candidates");
stats.RecordKill(false, "Minion"); stats.RecordKill(true, "Boss A"); stats.RecordKill(true, "Boss B");
Check(stats.Kills == 3 && stats.BossKills == 2 && stats.BossFinishes.SequenceEqual(new[] { "Boss A", "Boss B" }),
    "Boss finishes are included in enemy kills and retain named honors");
copy = stats.Clone(); copy.BossFinishes.Add("Not real");
Check(stats.BossFinishes.Count == 2, "Boss honors snapshots do not mutate live history");

string sessionA = RunIdentity.Create("seed", 1000, "Standard", "profile1", new ulong[] { 22, 11 });
Check(sessionA == RunIdentity.Create("seed", 1000, "Standard", "profile1", new ulong[] { 11, 22 }),
    "Reordered multiplayer roster restores the same session");
Check(sessionA != RunIdentity.Create("seed", 2000, "Standard", "profile1", new ulong[] { 11, 22 }) &&
    sessionA != RunIdentity.Create("seed", 1000, "Standard", "profile2", new ulong[] { 11, 22 }) &&
    sessionA != RunIdentity.Create("seed", 1000, "Standard", "profile1", new ulong[] { 11 }),
    "Repeated seeds, different profiles and different parties do not share totals");
string temp = Path.Combine(Path.GetTempPath(), "party-meter-checks-" + Guid.NewGuid().ToString("N"));
var archives = new RunArchiveStore(temp);
saved.StableRunId = sessionA;
saved.Players[0].RunSupport = stats.Clone();
archives.Save(saved);
string solo = RunIdentity.Create("solo-seed", 1001, "Standard", "profile1", new ulong[] { 11 });
archives.Save(new SavedState { StableRunId = solo, Players = new() { new SavedPlayer { PlayerKey = 11, TotalDamage = 55 } } });
Check(archives.Load(sessionA)!.Players[0].RunSupport.BossFinishes.Count == 2 && archives.Load(solo)!.Players[0].TotalDamage == 55,
    "Single-player and multiplayer checkpoints coexist across disk reloads");
var finished = new SavedState { StableRunId = "finished", Completed = true };
archives.Save(finished);
Check(archives.Load("finished") == null, "Completed archives are not restored as active runs");

// Exercise the actual menu/reset/resume path without executing gameplay or calling native Godot.
var service = typeof(RunDPSMeterService);
var flags = BindingFlags.NonPublic | BindingFlags.Static;
service.GetField("_archives", flags)!.SetValue(null, archives);
var totals = (Dictionary<ulong, PlayerDamageSnapshot>)service.GetField("Totals", flags)!.GetValue(null)!;
service.GetField("_stableRunId", flags)!.SetValue(null, solo);
totals[11] = new PlayerDamageSnapshot { PlayerKey = 11, DisplayName = "Tester", TotalDamage = 55 };
RunDPSMeterService.BeginCombat(null, null);
totals[11].TotalDamage += 20;
totals[11].RunSupport.RecordKill(true, "Test Boss");
totals[11].RunSupport.Deaths = 2;
totals[11].RunSupport.RecordHealing(7, true);
totals[11].CombatSupport.RecordKill(true, "Test Boss");
RunDPSMeterService.EndCombat();
RunDPSMeterService.ClearForMainMenu();
Check(RunDPSMeterService.BuildOverlayState().Players.Count == 0, "Main menu clears in-memory stats");
var loadState = service.GetMethod("TryLoadState", flags)!;
Check((bool)loadState.Invoke(null, new object[] { solo, "unused", new List<PlayerHandle>(), true })!, "Saved session reloads after menu clearing");
service.GetField("_stableRunId", flags)!.SetValue(null, solo);
Check(totals[11].TotalDamage == 75 && totals[11].RunSupport.BossKills == 1, "Resuming restores damage and named boss credit");
Check(totals[11].RunSupport.Deaths == 2 && totals[11].RunSupport.AllyHealing == 7,
    "Deaths and attributed healing survive menu clearing and session reload");
RunDPSMeterService.BeginCombat(null, null); // same room => replay, not a second copy of its totals
Check(totals[11].TotalDamage == 55 && totals[11].RunSupport.Kills == 0,
    "Replaying the saved fight rolls back its old contribution before counting it again");
Check(totals[11].RunSupport.Deaths == 0 && totals[11].RunSupport.HealingDone == 0,
    "Replayed fights do not double count deaths or healing");
service.GetField("_combatActive", flags)!.SetValue(null, true);
totals[11].TotalDamage += 999;
service.GetMethod("SaveState", flags)!.Invoke(null, new object[] { false });
RunDPSMeterService.ClearForMainMenu();
loadState.Invoke(null, new object[] { solo, "unused", new List<PlayerHandle>(), true });
Check(totals[11].TotalDamage == 55, "Interrupted fight restores its pre-combat checkpoint without partial double counting");
RunDPSMeterService.ClearForMainMenu();
archives.Save(new SavedState { StableRunId = "guest-clock", SessionGroup = "co-op", StartedAt = 1000 });
Check(archives.FindResumedMultiplayer("co-op", 1002)?.StableRunId == "guest-clock",
    "Guest session recognizes the host's slightly different saved start time on resume");
Check(archives.FindResumedMultiplayer("another-party", 1002) == null && archives.FindResumedMultiplayer("co-op", 2000) == null,
    "Clock reconciliation cannot cross parties or distant sessions");
archives.Save(new SavedState { StableRunId = "ambiguous-clock", SessionGroup = "co-op", StartedAt = 1001 });
Check(archives.FindResumedMultiplayer("co-op", 1002) == null, "Ambiguous session matches are rejected instead of merging stats");

// Patch an isolated test process, never the running game. This catches missing methods
// and Harmony parameter-name mismatches that a normal C# build cannot detect.
if (!args.Contains("--bindings", StringComparer.Ordinal))
{
    Console.WriteLine($"{checks} accounting/persistence checks passed; use --bindings under a game-compatible runtime to check Harmony hooks.");
    return;
}
var harmony = new Harmony("local.party-meter.compatibility-check");
var entry = typeof(ModEntry);
entry.GetField("_harmony", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, harmony);
var patchHook = entry.GetMethod("PatchHook", BindingFlags.NonPublic | BindingFlags.Static)!;
string[] hooks = { "BeforeCombatStart", "AfterCombatEnd", "AfterPlayerTurnStart", "BeforeCardPlayed", "AfterCardPlayed", "AfterDamageGiven", "AfterDamageReceived", "AfterBlockGained", "AfterDiedToDoom" };
foreach (var hook in hooks)
{
    patchHook.Invoke(null, new object[] { hook, hook + "Postfix" });
    Check(true, "Harmony binding: " + hook);
}
var supportPatch = entry.Assembly.GetType("DPSMeter.SupportHookPatches")!;
harmony.Patch(AccessTools.Method(typeof(Hook), "AfterPowerAmountChanged"), prefix: new HarmonyMethod(supportPatch, "PowerChanged"));
Check(true, "Harmony binding: ally buffs/debuffs");
entry.GetMethod("PatchPowerDamageMethods", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
Check(true, "All inherited power-damage hooks bind against installed game");
var patchMethod = entry.GetMethod("PatchMethod", BindingFlags.NonPublic | BindingFlags.Static)!;
patchMethod.Invoke(null, new object?[] { typeof(MegaCrit.Sts2.Core.Models.Powers.PoisonPower), "AfterSideTurnStart", "PoisonAfterSideTurnStartPrefix", "PoisonAfterSideTurnStartPostfix", null });
patchMethod.Invoke(null, new object?[] { typeof(MegaCrit.Sts2.Core.Models.Powers.DoomPower), "DoomKill", "DoomKillPrefix", null, null });
patchMethod.Invoke(null, new object?[] { typeof(MegaCrit.Sts2.Core.Runs.RunManager), "OnEnded", null, "RunEndedPostfix", entry.Assembly.GetType("DPSMeter.HookPatches") });
Check(true, "Poison, Doom and run completion hooks bind against installed game");
entry.GetMethod("PatchKillAndMenuHooks", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
Check(true, "Lethal hit, confirmed death, main menu, new run and resume hooks bind against installed game");
entry.GetMethod("PatchHealthHooks", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
Check(true, "Healing, source context, revival and shutdown hooks bind against installed game");
harmony.UnpatchAll(harmony.Id);
Console.WriteLine($"{checks} checks passed; no gameplay was executed.");
