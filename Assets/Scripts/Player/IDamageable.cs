/// <summary>
/// Receives damage from hazards, falls, and future combat sources.
/// Implementations should apply authority rules (server-only when networked).
/// </summary>
public interface IDamageable
{
    /// <summary>Applies the given damage amount to this target.</summary>
    /// <param name="amount">Damage points to remove; zero or negative should be ignored.</param>
    void ApplyDamage(int amount);
}
