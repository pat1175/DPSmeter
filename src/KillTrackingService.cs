using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace DPSMeter;

public static partial class RunDPSMeterService
{
    private static readonly KillCreditLedger<Creature> KillCredits = new();

    public static void CaptureKillingBlow(Creature? dealer, DamageResult? results, Creature? target, CardModel? cardSource)
    {
        target = results?.Receiver ?? target;
        if (target == null || !target.IsEnemy || results?.WasTargetKilled != true || results.UnblockedDamage <= 0) return;
        PlayerHandle handle;
        var poison = CurrentPoisonContext.Value;
        if (poison != null && dealer == null && cardSource == null && ReferenceEquals(poison.Target, target))
        {
            if (!ReflectionHelpers.TryResolvePlayerHandle(poison.Applier, out handle)) return;
        }
        else if (!TryResolveDamageDealerHandle(dealer, cardSource, out handle)) return;
        CaptureKillCandidate(target, handle);
    }

    private static void CaptureKillCandidate(Creature target, PlayerHandle handle)
    {
        if (!target.IsEnemy) return;
        // Snapshot this before death removes Minion/Illusion powers or detaches the creature.
        bool boss = target.CombatState?.Encounter?.RoomType == RoomType.Boss && target.IsPrimaryEnemy;
        lock (SyncRoot)
        {
            ApplyHandle(GetOrCreate(handle), handle);
            KillCredits.Capture(target, handle.PlayerKey, boss, target.Name);
        }
    }

    public static void ConfirmKill(Creature creature, bool wasRemovalPrevented)
    {
        lock (SyncRoot)
        {
            var credit = KillCredits.Confirm(creature, wasRemovalPrevented);
            if (credit == null || !Totals.TryGetValue(credit.PlayerKey, out var snapshot)) return;
            snapshot.CombatSupport.RecordKill(credit.IsBoss, credit.VictimName);
            snapshot.RunSupport.RecordKill(credit.IsBoss, credit.VictimName);
        }
        Publish();
    }

    // Clearing the display is distinct from deleting a saved run's meter checkpoint.
    // Save & Quit must be able to restore its completed-combat totals later.
    public static void ClearForMainMenu()
    {
        ResetTrackingMemory();
        Publish();
    }

    private static void ResetTrackingMemory()
    {
        lock (SyncRoot)
        {
            Totals.Clear();
            _completedState = null;
            SupportedCards.Clear();
            PendingDoomDamageByCreature.Clear();
            KillCredits.Clear();
            Deaths.Clear();
            HealingSource.Value = null;
            _beforeCombatState = null;
            _lastCombatKey = null;
            _sessionGroup = "";
            _startedAt = 0;
            _currentRunToken = null;
            _stableRunId = null;
            _combatIndex = 0;
            _combatActive = false;
            _activePlayerKey = null;
            _damageCounter = 0;
            CurrentPoisonContext.Value = null;
            CurrentSourceDamageContext.Value = null;
            CurrentCardDamageAggregation.Value = null;
        }
    }
}

internal static class KillTrackingPatches
{
    public static void DamageGivenPrefix(Creature? dealer, DamageResult? results, Creature? target, CardModel? cardSource)
    {
        try { RunDPSMeterService.CaptureKillingBlow(dealer, results, target, cardSource); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Kill observation failed: " + error.Message); }
    }

    public static void DeathPrefix(Creature creature, bool wasRemovalPrevented)
    {
        try
        {
            RunDPSMeterService.RecordPlayerDeath(creature, wasRemovalPrevented);
            RunDPSMeterService.ConfirmKill(creature, wasRemovalPrevented);
        }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Death observation failed: " + error.Message); }
    }

    public static void MainMenuPrefix()
    {
        try { RunDPSMeterService.ClearForMainMenu(); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Menu reset failed: " + error.Message); }
    }

    public static void RunLoadedPrefix(MegaCrit.Sts2.Core.Runs.RunState runState)
    {
        try { RunDPSMeterService.BeginRun(runState); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Session restoration failed: " + error.Message); }
    }

    public static void NewRunPrefix(MegaCrit.Sts2.Core.Runs.RunState runState)
    {
        try { RunDPSMeterService.BeginRun(runState, resuming: false); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] New session initialization failed: " + error.Message); }
    }
}
