namespace DPSMeter;

public sealed class DeathLedger<T> where T : class
{
    private readonly HashSet<T> _dead = new(ReferenceEqualityComparer.Instance);
    public bool Confirm(T creature, bool prevented, bool isDead) => !prevented && isDead && _dead.Add(creature);
    public void ObserveHealth(T creature, bool alive) { if (alive) _dead.Remove(creature); }
    public void Clear() => _dead.Clear();
}
