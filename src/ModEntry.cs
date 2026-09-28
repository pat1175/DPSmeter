using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace DPSMeter;

[ModInitializer("Initialize")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Initialize()
    {
        if (_harmony != null)
        {
            return;
        }

        _harmony = new Harmony("local.sts2.dps_meter");
        PatchHook(nameof(Hook.BeforeCombatStart), nameof(HookPatches.BeforeCombatStartPostfix));
        PatchHook(nameof(Hook.AfterCombatEnd), nameof(HookPatches.AfterCombatEndPostfix));
        PatchHook(nameof(Hook.AfterPlayerTurnStart), nameof(HookPatches.AfterPlayerTurnStartPostfix));
        PatchHook(nameof(Hook.BeforeCardPlayed), nameof(HookPatches.BeforeCardPlayedPostfix));
        PatchHook(nameof(Hook.AfterCardPlayed), nameof(HookPatches.AfterCardPlayedPostfix));
        PatchHook(nameof(Hook.AfterDamageGiven), nameof(HookPatches.AfterDamageGivenPostfix));
        PatchHook(nameof(Hook.AfterDamageReceived), nameof(HookPatches.AfterDamageReceivedPostfix));
        PatchHook(nameof(Hook.AfterBlockGained), nameof(HookPatches.AfterBlockGainedPostfix));
        // Observe the resolved power delta before reactions can change its owner or amount.
        _harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.AfterPowerAmountChanged)),
            prefix: new HarmonyMethod(typeof(SupportHookPatches), nameof(SupportHookPatches.PowerChanged)));
        PatchHook(nameof(Hook.AfterDiedToDoom), nameof(HookPatches.AfterDiedToDoomPostfix));
        PatchMethod(typeof(PoisonPower), nameof(PoisonPower.AfterSideTurnStart), nameof(StatusPowerPatches.PoisonAfterSideTurnStartPrefix), nameof(StatusPowerPatches.PoisonAfterSideTurnStartPostfix));
        PatchMethod(typeof(DoomPower), nameof(DoomPower.DoomKill), nameof(StatusPowerPatches.DoomKillPrefix));
        PatchMethod(typeof(RunManager), nameof(RunManager.OnEnded), postfixName: nameof(HookPatches.RunEndedPostfix), patchType: typeof(HookPatches));
        PatchPowerDamageMethods();
        PatchKillAndMenuHooks();
        PatchHealthHooks();

        // Write to log file for debugging
        string logDir = OS.GetUserDataDir();
        string logPath = Path.Combine(logDir, "..", "DPSMeter_debug.log");
        using (var log = Godot.FileAccess.Open(logPath, Godot.FileAccess.ModeFlags.WriteRead))
        {
            if (log != null)
            {
                log.StoreLine($"[{DateTime.Now:HH:mm:ss}] DPSMeter Init - hooks: {_harmony?.GetPatchedMethods().Count() ?? 0}");
            }
        }
        GD.Print("[DPSMeter] Log path: " + logPath);
        Log.Info("DPSMeter initialized");
        DPSMeterOverlay.EnsureCreated();
    }

    private static void PatchHook(string hookName, string postfixName)
    {
        MethodInfo original = AccessTools.Method(typeof(Hook), hookName)
            ?? throw new MissingMethodException(typeof(Hook).FullName, hookName);
        MethodInfo postfix = AccessTools.Method(typeof(HookPatches), postfixName)
            ?? throw new MissingMethodException(typeof(HookPatches).FullName, postfixName);

        _harmony!.Patch(original, postfix: new HarmonyMethod(postfix));
    }

    private static void PatchKillAndMenuHooks()
    {
        _harmony!.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.AfterDamageGiven)),
            prefix: new HarmonyMethod(typeof(KillTrackingPatches), nameof(KillTrackingPatches.DamageGivenPrefix)));
        _harmony.Patch(AccessTools.Method(typeof(Hook), nameof(Hook.AfterDeath)),
            prefix: new HarmonyMethod(typeof(KillTrackingPatches), nameof(KillTrackingPatches.DeathPrefix)));
        _harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.NGame), "LoadMainMenu"),
            prefix: new HarmonyMethod(typeof(KillTrackingPatches), nameof(KillTrackingPatches.MainMenuPrefix)));
        _harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.NGame), "StartRun"),
            prefix: new HarmonyMethod(typeof(KillTrackingPatches), nameof(KillTrackingPatches.NewRunPrefix)));
        _harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.NGame), "LoadRun"),
            prefix: new HarmonyMethod(typeof(KillTrackingPatches), nameof(KillTrackingPatches.RunLoadedPrefix)));
    }

    private static void PatchHealthHooks()
    {
        var patch = typeof(HealthTrackingPatches);
        _harmony!.Patch(AccessTools.Method(typeof(Creature), "HealInternal"),
            prefix: new HarmonyMethod(patch, "HealPrefix"), postfix: new HarmonyMethod(patch, "HealPostfix"));
        _harmony.Patch(AccessTools.Method(typeof(Creature), "SetCurrentHpInternal"),
            postfix: new HarmonyMethod(patch, "HealthChangedPostfix"));
        _harmony.Patch(AccessTools.Method(typeof(PlayerChoiceContext), "PushModel"), postfix: new HarmonyMethod(patch, "PushModelPostfix"));
        _harmony.Patch(AccessTools.Method(typeof(PlayerChoiceContext), "PopModel"), postfix: new HarmonyMethod(patch, "PopModelPostfix"));
        _harmony.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.NGame), "Quit"), prefix: new HarmonyMethod(patch, "QuitPrefix"));
    }

    private static void PatchMethod(Type ownerType, string methodName, string? prefixName = null, string? postfixName = null, Type? patchType = null)
    {
        Type resolvedPatchType = patchType ?? typeof(StatusPowerPatches);
        MethodInfo original = AccessTools.Method(ownerType, methodName)
            ?? throw new MissingMethodException(ownerType.FullName, methodName);
        HarmonyMethod? prefix = prefixName != null
            ? new HarmonyMethod(AccessTools.Method(resolvedPatchType, prefixName)
                ?? throw new MissingMethodException(resolvedPatchType.FullName, prefixName))
            : null;
        HarmonyMethod? postfix = postfixName != null
            ? new HarmonyMethod(AccessTools.Method(resolvedPatchType, postfixName)
                ?? throw new MissingMethodException(resolvedPatchType.FullName, postfixName))
            : null;

        _harmony!.Patch(original, prefix: prefix, postfix: postfix);
    }

    private static void PatchPowerDamageMethods()
    {
        PatchPowerDamageMethod(typeof(FlameBarrierPower), nameof(FlameBarrierPower.AfterDamageReceived));
        PatchPowerDamageMethod(typeof(OutbreakPower), nameof(OutbreakPower.AfterPowerAmountChanged));
        PatchPowerDamageMethod(typeof(ConstrictPower), nameof(ConstrictPower.AfterSideTurnEnd));
        PatchPowerDamageMethod(typeof(ThornsPower), nameof(ThornsPower.BeforeDamageReceived));
        PatchPowerDamageMethod(typeof(NecroMasteryPower), nameof(NecroMasteryPower.AfterCurrentHpChanged));
        PatchPowerDamageMethod(typeof(MagicBombPower), nameof(MagicBombPower.AfterSideTurnEnd));
        PatchPowerDamageMethod(typeof(RollingBoulderPower), nameof(RollingBoulderPower.AfterPlayerTurnStart));
        PatchPowerDamageMethod(typeof(PanachePower), nameof(PanachePower.AfterCardPlayed));
        PatchPowerDamageMethod(typeof(BlackHolePower), "DealDamageToAllEnemies");
        PatchPowerDamageMethod(typeof(DemisePower), nameof(DemisePower.AfterSideTurnEnd));
        PatchPowerDamageMethod(typeof(ThunderPower), nameof(ThunderPower.AfterOrbEvoked));
        PatchPowerDamageMethod(typeof(GalvanicPower), nameof(GalvanicPower.AfterCardPlayed));
        PatchPowerDamageMethod(typeof(SpeedsterPower), nameof(SpeedsterPower.AfterCardDrawn));
        PatchPowerDamageMethod(typeof(SerpentFormPower), nameof(SerpentFormPower.AfterCardPlayed));
        PatchPowerDamageMethod(typeof(HailstormPower), nameof(HailstormPower.BeforeSideTurnEnd));
        PatchPowerDamageMethod(typeof(DisintegrationPower), nameof(DisintegrationPower.AfterSideTurnEndLate));
        PatchPowerDamageMethod(typeof(SmokestackPower), nameof(SmokestackPower.AfterCardGeneratedForCombat));
        PatchPowerDamageMethod(typeof(TheBombPower), nameof(TheBombPower.BeforeSideTurnEnd));
        PatchPowerDamageMethod(typeof(JuggernautPower), nameof(JuggernautPower.AfterBlockGained));
        PatchPowerDamageMethod(typeof(InfernoPower), nameof(InfernoPower.AfterPlayerTurnStart));
        PatchPowerDamageMethod(typeof(InfernoPower), nameof(InfernoPower.AfterDamageReceived));
        PatchPowerDamageMethod(typeof(ReflectPower), nameof(ReflectPower.AfterDamageReceived));
        PatchPowerDamageMethod(typeof(StranglePower), nameof(StranglePower.AfterCardPlayed));
        PatchPowerDamageMethod(typeof(CrimsonMantlePower), nameof(CrimsonMantlePower.AfterPlayerTurnStart));
        PatchPowerDamageMethod(typeof(SleightOfFleshPower), nameof(SleightOfFleshPower.AfterPowerAmountChanged));
        PatchPowerDamageMethod(typeof(GravityPower), nameof(GravityPower.AfterCardPlayed));
        PatchPowerDamageMethod(typeof(HauntPower), nameof(HauntPower.AfterCardPlayed));
    }

    private static void PatchPowerDamageMethod(Type ownerType, string methodName)
    {
        PatchMethod(
            ownerType,
            methodName,
            nameof(PowerDamagePatches.PowerDamagePrefix),
            nameof(PowerDamagePatches.PowerDamagePostfix),
            typeof(PowerDamagePatches));
    }
}

internal static class HookPatches
{
    private static void EnsureOverlayCreated()
    {
        DPSMeterOverlay.EnsureCreated();
    }

    // Hook signature:
    // BeforeCombatStart(IRunState runState, CombatState? combatState)
    public static void BeforeCombatStartPostfix(IRunState? runState, CombatState? combatState)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.BeginRun(runState);
        RunDPSMeterService.BeginCombat(runState, combatState);
    }

    // Hook signature:
    // AfterCombatEnd(IRunState runState, CombatState? combatState, CombatRoom room)
    public static void AfterCombatEndPostfix(IRunState? runState, CombatState? combatState)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.EndCombat();
    }

    // Hook signature:
    // AfterPlayerTurnStart(CombatState combatState, PlayerChoiceContext choiceContext, Player player)
    public static void AfterPlayerTurnStartPostfix(CombatState combatState, PlayerChoiceContext? choiceContext, Player player)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.EnsurePlayersRegistered(combatState.RunState, combatState);
        RunDPSMeterService.NotePlayer(player);
    }

    // Hook signature:
    // BeforeCardPlayed(ICombatState combatState, CardPlay cardPlay)
    public static void BeforeCardPlayedPostfix(ICombatState? combatState, CardPlay? cardPlay)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.EnsurePlayersRegistered(combatState);
        RunDPSMeterService.BeginCardDamageAggregation(cardPlay);
    }

    // Hook signature:
    // AfterCardPlayed(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    public static void AfterCardPlayedPostfix(ICombatState? combatState, PlayerChoiceContext? choiceContext, CardPlay? cardPlay)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.EnsurePlayersRegistered(combatState);
        RunDPSMeterService.CompleteCardDamageAggregation(cardPlay);
        RunDPSMeterService.RecordCardPlayed(cardPlay);
    }

    // Hook signature:
    // AfterDamageGiven(PlayerChoiceContext choiceContext, CombatState combatState, 
    //                   Creature? dealer, DamageResult results, ValueProp props, 
    //                   Creature target, CardModel? cardSource)
    public static void AfterDamageGivenPostfix(
        PlayerChoiceContext? choiceContext,
        CombatState? combatState,
        Creature? dealer,
        DamageResult? results,
        ValueProp props,
        Creature? target,
        CardModel? cardSource)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.RecordDamageReceivedFromDamageGiven(target, results);

        if (dealer != null &&
            !ReflectionHelpers.IsPlayerCreature(dealer) &&
            !RunDPSMeterService.HasActivePlayerDamageSource())
        {
            return;
        }

        RunDPSMeterService.EnsurePlayersRegistered(combatState?.RunState, combatState);

        if (RunDPSMeterService.TryRecordPoisonDamage(dealer, target, cardSource, results))
            return;

        RunDPSMeterService.RecordDamage(dealer, results, target, cardSource);
    }

    // Hook signature:
    // AfterDamageReceived(PlayerChoiceContext choiceContext, IRunState runState, ICombatState? combatState,
    //                     Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    public static void AfterDamageReceivedPostfix(
        PlayerChoiceContext? choiceContext,
        IRunState? runState,
        ICombatState? combatState,
        Creature? target,
        DamageResult? result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.EnsurePlayersRegistered(runState, combatState);
        RunDPSMeterService.RecordDamageReceived(target, result);
    }

    // Hook signature:
    // AfterBlockGained(ICombatState combatState, Creature creature, decimal amount, ValueProp props, CardModel? cardSource)
    public static void AfterBlockGainedPostfix(
        ICombatState? combatState,
        Creature? creature,
        decimal amount,
        ValueProp props,
        CardModel? cardSource)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.EnsurePlayersRegistered(combatState);
        RunDPSMeterService.RecordBlockGained(creature, amount);
        SupportHookPatches.BlockGiven(creature, amount, cardSource);
    }

    // RunManager.OnEnded(bool isVictory)
    public static void RunEndedPostfix(bool isVictory)
    {
        EnsureOverlayCreated();
        RunDPSMeterService.EndRun();
    }

    // Hook signature:
    // AfterDiedToDoom(CombatState combatState, IReadOnlyList<Creature> creatures)
    public static void AfterDiedToDoomPostfix(CombatState combatState, System.Collections.Generic.IReadOnlyList<Creature>? creatures)
    {
        EnsureOverlayCreated();
        // Attribute Doom kill damage to the player who applied Doom.
        if (creatures != null)
        {
            RunDPSMeterService.RecordDoomDamage(combatState, creatures);
        }
    }
}

internal static class PowerDamagePatches
{
    public static void PowerDamagePrefix(PowerModel __instance, out object? __state)
    {
        __state = RunDPSMeterService.BeginPowerDamageTracking(__instance);
    }

    public static void PowerDamagePostfix(ref Task __result, object? __state)
    {
        __result = RunDPSMeterService.CompletePowerDamageTrackingAsync(__result, __state);
    }
}

internal static class StatusPowerPatches
{
    public static void PoisonAfterSideTurnStartPrefix(PoisonPower __instance, CombatSide side, out object? __state)
    {
        __state = RunDPSMeterService.BeginPoisonTracking(__instance, side);
    }

    public static void PoisonAfterSideTurnStartPostfix(ref Task __result, object? __state)
    {
        __result = RunDPSMeterService.CompletePoisonTrackingAsync(__result, __state);
    }

    public static void DoomKillPrefix(System.Collections.Generic.IReadOnlyList<Creature> creatures)
    {
        RunDPSMeterService.CapturePendingDoomDamage(creatures);
    }
}
