using System.Reflection;
using DPSMeter;
using MegaCrit.Sts2.Core.Entities.Creatures;

var resolve = typeof(ModEntry).Assembly.GetType("DPSMeter.ReflectionHelpers")!
    .GetMethod("TryResolveDamageAmount", BindingFlags.Public | BindingFlags.Static)!;
foreach (var (block, hp, overkill, expected) in new[]
{
    (20, 10, 0, 30), // Partially blocked hit.
    (30, 0, 0, 30),  // Fully blocked hit.
    (0, 30, 0, 30),  // Unblocked hit.
    (20, 5, 5, 25),  // Do not credit overkill.
    (0, 0, 0, 0)
})
{
    var result = new DamageResult(null!, default)
        { BlockedDamage = block, UnblockedDamage = hp, OverkillDamage = overkill };
    object?[] arguments = { result, 0m };
    bool accepted = (bool)resolve.Invoke(null, arguments)!;
    if ((decimal)arguments[1]! != expected || accepted != (expected > 0))
        throw new Exception($"Incorrect credited damage: block={block}, hp={hp}, overkill={overkill}");
}
Console.WriteLine("5 outgoing-damage checks passed; no gameplay was executed.");
