using System.Threading;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace DPSMeter;

public static partial class RunDPSMeterService
{
    private static readonly DeathLedger<Creature> Deaths = new();
    private static readonly AsyncLocal<HealingSourceFrame?> HealingSource = new();

    internal static void PushHealingSource(PlayerChoiceContext context, AbstractModel model) =>
        HealingSource.Value = new(context, model, HealingSource.Value);

    internal static void PopHealingSource(PlayerChoiceContext context, AbstractModel model)
    {
        var frame = HealingSource.Value;
        if (frame != null && ReferenceEquals(frame.Context, context) && ReferenceEquals(frame.Model, model))
            HealingSource.Value = frame.Previous;
    }

    public static void ObservePlayerHealth(Creature creature)
    {
        lock (SyncRoot) Deaths.ObserveHealth(creature, creature.CurrentHp > 0);
    }

    public static void RecordPlayerDeath(Creature creature, bool prevented)
    {
        if (string.IsNullOrEmpty(_currentRunToken) || !creature.IsPlayer || RunManager.Instance.IsAbandoned ||
            !ReflectionHelpers.TryResolvePlayerHandle(creature, out var handle)) return;
        lock (SyncRoot)
        {
            if (!Deaths.Confirm(creature, prevented, creature.IsDead)) return;
            var player = GetOrCreate(handle);
            player.RunSupport.Deaths++;
            if (_combatActive) player.CombatSupport.Deaths++;
        }
        if (!_combatActive) SaveState();
        Publish();
    }

    public static void RecordHealing(Creature creature, int before)
    {
        ObservePlayerHealth(creature);
        decimal amount = SupportMetrics.EffectiveHealing(before, creature.CurrentHp);
        if (amount <= 0 || !creature.IsPlayer || string.IsNullOrEmpty(_currentRunToken) ||
            !ReflectionHelpers.TryResolvePlayerHandle(creature, out var recipient)) return;
        var frame = HealingSource.Value;
        // Match the game's actual executing model; never guess from the last active player.
        AbstractModel? model = frame != null && ReferenceEquals(frame.Context.LastInvolvedModel, frame.Model)
            ? frame.Model : null;
        object? source = model is PowerModel power ? power.Applier ?? power.Owner : model;
        lock (SyncRoot)
        {
            var target = GetOrCreate(recipient);
            target.RunSupport.HealingReceived += amount;
            if (_combatActive) target.CombatSupport.HealingReceived += amount;
            if (source != null && ReflectionHelpers.TryResolvePlayerHandle(source, out var provider))
            {
                var player = GetOrCreate(provider);
                bool ally = provider.PlayerKey != recipient.PlayerKey;
                player.RunSupport.RecordHealing(amount, ally);
                if (_combatActive) player.CombatSupport.RecordHealing(amount, ally);
                if (ally && model is CardModel card) MarkAllySupport(card);
            }
        }
        if (!_combatActive) SaveState();
        Publish();
    }

    private sealed record HealingSourceFrame(PlayerChoiceContext Context, AbstractModel Model, HealingSourceFrame? Previous);
}

internal static class HealthTrackingPatches
{
    public static void PushModelPostfix(PlayerChoiceContext __instance, AbstractModel model) => RunDPSMeterService.PushHealingSource(__instance, model);
    public static void PopModelPostfix(PlayerChoiceContext __instance, AbstractModel model) => RunDPSMeterService.PopHealingSource(__instance, model);
    public static void HealPrefix(Creature __instance, out int __state) => __state = __instance.CurrentHp;
    public static void HealPostfix(Creature __instance, int __state)
    {
        try { RunDPSMeterService.RecordHealing(__instance, __state); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Healing observation failed: " + error.Message); }
    }
    public static void HealthChangedPostfix(Creature __instance)
    {
        try { RunDPSMeterService.ObservePlayerHealth(__instance); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Revival observation failed: " + error.Message); }
    }
    public static void QuitPrefix()
    {
        try { DPSMeterOverlay.Shutdown(); RunDPSMeterService.ClearForMainMenu(); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Shutdown cleanup failed: " + error.Message); }
    }
}
