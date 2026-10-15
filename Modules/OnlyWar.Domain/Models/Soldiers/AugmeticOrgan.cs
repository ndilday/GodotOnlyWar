namespace OnlyWar.Domain.Soldiers;

/// <summary>
/// One augmetic organ inside an organic vital location (Design/Active/VitalLocationReplacement.md
/// §3.4). A cybernetic organ replacement adds one to the Torso or the Vitals; the location itself
/// stays organic. The organ has its own wound track and its own constitution, but no armor: a hit
/// reaches it only after it has passed the soldier's armor and the location's natural armor.
///
/// It uses its host location's cripple and sever thresholds. It never heals naturally; a
/// Techmarine repairs it, as he repairs any other augmetic.
/// </summary>
public sealed class AugmeticOrgan
{
    internal event System.Action Changed;

    /// <summary>
    /// Stable identity within the host location. A repair job names the organ by this index, so it
    /// is never reused while the organ exists and never renumbered when another organ is removed.
    /// </summary>
    public int Index { get; }
    public Wounds Wounds { get; }
    /// <summary>Zero means no value of its own, and the soldier's constitution applies.</summary>
    public float Constitution { get; }
    public HitLocation Host { get; internal set; }

    public AugmeticOrgan(int index, float constitution, uint woundTotal = 0, uint weeksOfHealing = 0)
    {
        Index = index;
        Constitution = constitution;
        Wounds = new Wounds(woundTotal, weeksOfHealing);
        Wounds.Changed += () => Changed?.Invoke();
    }

    public bool IsCrippled => Host != null && Wounds.WoundTotal >= Host.Template.CrippleWound;

    public bool IsSevered => Host != null && Wounds.WoundTotal >= Host.Template.SeverWound;

    /// <summary>The constitution a wound to this organ is measured against.</summary>
    public float EffectiveConstitution(ISoldier soldier) =>
        Constitution > 0f ? Constitution : soldier.Constitution;

    /// <summary>A display name, such as "Vitals augmetic organ 2".</summary>
    public string Name => $"{Host?.Template?.Name ?? "Vital"} augmetic organ {Index}";
}
