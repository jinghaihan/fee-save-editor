using System.Globalization;
using FeeEditor.Core;
using FeeEditor.Gui.Localization;

namespace FeeEditor.Gui;

public partial class MainWindow
{
    public sealed record BondRingSkillEffect(string Name, string Description);

    private void RefreshBondRingEffects(BondRing? ring)
    {
        var definition = ring is null ? null : EmblemCatalog.Ring(ring.RingHash);
        BondRingEffects.IsVisible = definition is not null;
        BondRingSkillEffects.IsVisible = definition?.Skills.Count > 0;
        BondRingStatBonuses.ItemsSource = definition?.StatBonuses
            .Select((value, index) => new { Stat = (RosterStat)index, Value = value })
            .Where(bonus => bonus.Value != 0)
            .Select(bonus => new EngravingEffect(UiLanguage.Get(bonus.Stat.ToString()),
                bonus.Value.ToString("+0;-0;0", CultureInfo.InvariantCulture))).ToArray() ?? [];
        BondRingSkills.ItemsSource = definition?.Skills
            .Select(skill => new BondRingSkillEffect(skill.Name(UiLanguage.Current), skill.Description(UiLanguage.Current)))
            .ToArray() ?? [];
    }
}
