using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace DPSMeter;

public static partial class RunDPSMeterService
{
    private static readonly HashSet<CardPlay> SupportedCards = new(ReferenceEqualityComparer.Instance);

    private static void MarkAllySupport(CardModel? card)
    {
        for (var context = CurrentCardDamageAggregation.Value; context != null; context = context.Previous)
        {
            if (ReferenceEquals(context.CardPlay.Card, card))
            {
                SupportedCards.Add(context.CardPlay);
                break;
            }
        }
    }

    public static void RecordBlockGiven(Creature? recipient, decimal amount, CardModel? cardSource)
    {
        // This API supplies a card source, but no reliable source for many passive effects.
        // Do not infer the provider from the last active player or the recipient.
        if (recipient == null || decimal.Truncate(amount) <= 0 ||
            !ReflectionHelpers.IsPlayerCreature(recipient) ||
            !ReflectionHelpers.TryResolvePlayerHandle(recipient, out var target) ||
            !ReflectionHelpers.TryResolvePlayerHandle(cardSource, out var source)) return;
        lock (SyncRoot)
        {
            var snapshot = GetOrCreate(source);
            bool ally = target.PlayerKey != source.PlayerKey;
            snapshot.CombatSupport.RecordBlock(amount, ally);
            snapshot.RunSupport.RecordBlock(amount, ally);
            if (ally) MarkAllySupport(cardSource);
        }
        Publish();
    }

    public static void RecordPowerContribution(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        // Count positive additions only. Decay, removal, and cleansing are separate mechanics.
        if (decimal.Truncate(amount) <= 0 || power.Owner == null) return;
        PlayerHandle source;
        if (!ReflectionHelpers.TryResolvePlayerHandle(cardSource, out source) &&
            !ReflectionHelpers.TryResolvePlayerHandle(applier, out source)) return;
        bool ally = ReflectionHelpers.IsPlayerCreature(power.Owner) &&
            ReflectionHelpers.TryResolvePlayerHandle(power.Owner, out var target) && target.PlayerKey != source.PlayerKey;
        bool buff = ally && power.TypeForCurrentAmount == PowerType.Buff;
        bool debuff = power.Owner.IsMonster && !ReflectionHelpers.IsPlayerCreature(power.Owner) &&
            power.TypeForCurrentAmount == PowerType.Debuff;
        if (!buff && !debuff) return;
        lock (SyncRoot)
        {
            var snapshot = GetOrCreate(source);
            foreach (var stats in new[] { snapshot.CombatSupport, snapshot.RunSupport })
            {
                if (buff) stats.RecordBuff(power.Title.GetFormattedText(), amount);
                if (debuff) stats.EnemyDebuffs++;
            }
            if (buff) MarkAllySupport(cardSource);
        }
        Publish();
    }

    public static string BuildShareSummary(bool run)
    {
        var state = BuildOverlayState();
        var text = new StringBuilder(DPSMeterOverlay.L("SUMMARY_TITLE"));
        text.AppendLine(" | " + DPSMeterOverlay.L(run ? "RUN_SCOPE" : "COMBAT"));
        foreach (var player in state.Players.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var stats = run ? player.RunSupport : player.CombatSupport;
            text.AppendLine($"{player.DisplayName} ({player.CharacterName})");
            text.AppendLine($"  {DPSMeterOverlay.L("DAMAGE")}: {Format(run ? player.TotalDamage : player.CombatDamage)} | " +
                $"{DPSMeterOverlay.L("BLOCK_GIVEN")}: {Format(stats.BlockGiven)} | {DPSMeterOverlay.L("ALLY_BLOCK")}: {Format(stats.AllyBlock)}");
            text.AppendLine($"  {DPSMeterOverlay.L("SKILL")}: {stats.Skills} | {DPSMeterOverlay.L("POWER")}: {stats.Powers} | " +
                $"{DPSMeterOverlay.L("ALLY_SKILLS")}: {stats.AllySkillPlays} | {DPSMeterOverlay.L("ALLY_BUFFS")}: {stats.AllyBuffs} | " +
                $"{DPSMeterOverlay.L("DEBUFFS")}: {stats.EnemyDebuffs}");
            text.AppendLine($"  {DPSMeterOverlay.L("BLOCKED")}: {Format(stats.Blocked)} | {DPSMeterOverlay.L("HP_LOST")}: {Format(stats.HpLost)}");
            text.AppendLine($"  {DPSMeterOverlay.L("KILLS")}: {stats.Kills} | {DPSMeterOverlay.L("BOSS_KILLS")}: {stats.BossKills} | {DPSMeterOverlay.L("DEATHS")}: {stats.Deaths}");
            text.AppendLine($"  {DPSMeterOverlay.L("HEALING_DONE")}: {Format(stats.HealingDone)} | {DPSMeterOverlay.L("ALLY_HEALING")}: {Format(stats.AllyHealing)} | {DPSMeterOverlay.L("HEALING_RECEIVED")}: {Format(stats.HealingReceived)}");
            if (stats.BossFinishes.Count > 0)
                text.AppendLine($"  {DPSMeterOverlay.L("BOSS_HONORS")}: {string.Join(", ", stats.BossFinishes)}");
            foreach (var buff in stats.AllyBuffStacks.OrderBy(p => p.Key))
                text.AppendLine($"    {buff.Key}: +{Format(buff.Value)}");
        }
        text.AppendLine(DPSMeterOverlay.L("SUMMARY_NOTE"));
        return text.ToString();
    }
}

internal static class SupportHookPatches
{
    public static void PowerChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        try { RunDPSMeterService.RecordPowerContribution(power, amount, applier, cardSource); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Support observation failed: " + error.Message); }
    }

    public static void BlockGiven(Creature? creature, decimal amount, CardModel? cardSource)
    {
        try { RunDPSMeterService.RecordBlockGiven(creature, amount, cardSource); }
        catch (Exception error) { GD.PrintErr("[DPSMeter] Block observation failed: " + error.Message); }
    }
}
