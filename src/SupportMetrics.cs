namespace DPSMeter;

// Pure accounting: no game state, networking, or Godot dependencies.
public sealed class SupportMetrics
{
    public int Cards { get; set; }
    public int Attacks { get; set; }
    public int Skills { get; set; }
    public int Powers { get; set; }
    public int Auto { get; set; }
    public int AllySkillPlays { get; set; }
    public decimal BlockGiven { get; set; }
    public decimal AllyBlock { get; set; }
    public int AllyBuffs { get; set; }
    public int EnemyDebuffs { get; set; }
    public decimal BlockReceived { get; set; }
    public decimal Incoming { get; set; }
    public decimal Blocked { get; set; }
    public decimal HpLost { get; set; }
    public decimal MaxHpLost { get; set; }
    public int Kills { get; set; }
    public int BossKills { get; set; }
    public int Deaths { get; set; }
    public decimal HealingDone { get; set; }
    public decimal AllyHealing { get; set; }
    public decimal HealingReceived { get; set; }

    public static decimal EffectiveHealing(decimal before, decimal after) => Math.Max(0m, after - before);

    public void RecordHealing(decimal amount, bool ally)
    {
        if (amount <= 0) return;
        HealingDone += amount;
        if (ally) AllyHealing += amount;
    }
    public List<string> BossFinishes { get; set; } = new();
    public Dictionary<string, decimal> AllyBuffStacks { get; set; } = new();

    public void RecordCard(string type, bool auto, bool supportedAlly)
    {
        Cards++;
        if (auto) Auto++;
        if (type == "Attack") Attacks++;
        if (type == "Power") Powers++;
        if (type == "Skill")
        {
            Skills++;
            if (supportedAlly) AllySkillPlays++;
        }
    }

    public void RecordBlock(decimal amount, bool ally)
    {
        // Creature.GainBlockInternal uses the integer part of the resolved amount.
        amount = decimal.Truncate(amount);
        if (amount <= 0) return;
        BlockGiven += amount;
        if (ally) AllyBlock += amount;
    }

    public void RecordBuff(string name, decimal amount)
    {
        amount = decimal.Truncate(amount);
        if (amount <= 0) return;
        AllyBuffs++;
        AllyBuffStacks[name] = AllyBuffStacks.GetValueOrDefault(name) + amount;
    }

    public SupportMetrics Clone()
    {
        var copy = (SupportMetrics)MemberwiseClone();
        copy.AllyBuffStacks = new(AllyBuffStacks);
        copy.BossFinishes = new(BossFinishes);
        return copy;
    }

    public void RecordKill(bool isBoss, string name)
    {
        Kills++;
        if (!isBoss) return;
        BossKills++;
        BossFinishes.Add(name);
    }
}
