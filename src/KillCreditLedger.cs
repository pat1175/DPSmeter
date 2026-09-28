namespace DPSMeter;

/// <summary>Match a lethal event to a confirmed death. A death without a sourced hit is uncredited.</summary>
public sealed class KillCreditLedger<T> where T : class
{
    private readonly Dictionary<T, KillCredit> _pending = new(ReferenceEqualityComparer.Instance);

    public void Capture(T victim, ulong playerKey, bool isBoss, string victimName)
        => _pending[victim] = new(playerKey, isBoss, victimName);

    public KillCredit? Confirm(T victim, bool prevented)
    {
        if (!_pending.Remove(victim, out var credit)) return null;
        return prevented ? null : credit;
    }

    public void Clear() => _pending.Clear();
}

public sealed record KillCredit(ulong PlayerKey, bool IsBoss, string VictimName);
