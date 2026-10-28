using KWin.Controls;
using KWin.Core;
using KWin.Models;
using KWin.Services;
using KWin.Themes;
using KWin.Utils;
using static KWin.Utils.Loc;

namespace KWin.UI.Pages
{
    /// <summary>What the setting cards ask the main window to do.</summary>
    public sealed class SettingActions
    {
        public required Func<SettingDefinition, SettingState> Read { get; init; }
        /// <summary>Applies On/Off with preview, safety workflow, verification and undo.</summary>
        public required Func<SettingDefinition, bool, Task> Apply { get; init; }
        /// <summary>Undo description for settings whose apply also has a non-registry step.</summary>
        public Func<SettingDefinition, string?> UndoText { get; init; } = _ => null;
    }

    internal static class CardFactory
    {
        public static SettingCard Setting(SettingDefinition def, SettingActions a, Action refreshPage) =>
            new(def, a.Read, async (d, on) => { await a.Apply(d, on); refreshPage(); }, a.UndoText(def));
    }

    // =====================================================================================
    // Performance
    // =====================================================================================

    public sealed class PerformanceActions
    {
        public required SettingActions Settings { get; init; }
        public required Func<Task<List<(string Guid, string Name, bool Active)>>> LoadPowerPlans { get; init; }
        public required Func<string, Task> ApplyPowerPlan { get; init; }
        public required Func<Task> CreateUltimatePlan { get; init; }
        public required Func<Task<Result<(bool Enabled, MemoryManager.CompressionSource Source)>>> GetMemoryCompression { get; init; }
        public required Func<bool, Task> SetMemoryCompression { get; init; }
        public required Func<Task> RunBoost { get; init; }
    }

    public sealed class PerformancePage : CardPage
    {
        private readonly PerformanceActions _a;
        private readonly ActionCard _power, _compression;
        private readonly ThemedComboBox _plans = new() { Width = 260, Font = UiFonts.Regular(9.5f) };
        private readonly AccentButton _compOn, _compOff;

        public PerformancePage(PerformanceActions a)
        {
            _a = a;

            // Power plan
            _power = new ActionCard(Glyphs.Bolt, Get("perfp.power_title"), Get("perfp.power_desc"));
            _power.SetFact(Get("card.recommended"), Get("perfp.power_recommended"));
            _power.SetFact(Get("card.benefit"), Get("perfp.power_benefit"));
            _power.SetFact(Get("card.side_effects"), Get("perfp.power_side"));
            _power.SetFact(Get("card.requires"), Get("card.requires_user"));
            _power.SetFact(Get("card.undo"), Get("card.undo_session"));
            _plans.Height = 28;
            _power.SetExtra(_plans);
            _power.AddButton(Get("perfp.power_apply"), true, async () =>
            {
                if (_plans.SelectedItem is PlanItem p) { await _a.ApplyPowerPlan(p.Guid); await LoadPlansAsync(); }
            });
            _power.AddButton(Get("perfp.power_ultimate"), false, async () => { await _a.CreateUltimatePlan(); await LoadPlansAsync(); });
            AddCard(_power);

            foreach (var def in SettingCatalog.InGroup(SettingGroup.Performance))
                AddCard(CardFactory.Setting(def, a.Settings, RefreshAll));

            // Memory compression (Windows' supported MMAgent mechanism)
            _compression = new ActionCard(Glyphs.Memory, Get("perfp.comp_title"), Get("perfp.comp_desc"));
            _compression.SetFact(Get("card.recommended"), Get("perfp.comp_on"));
            _compression.SetFact(Get("card.benefit"), Get("perfp.comp_benefit"));
            _compression.SetFact(Get("card.side_effects"), Get("perfp.comp_side"));
            _compression.SetFact(Get("card.requires"), Get("card.requires_admin") + " · " + Get("card.restart_needed"));
            _compression.SetFact(Get("card.undo"), Get("card.undo_session"));
            _compOn = _compression.AddButton(Get("card.switch_to", Get("perfp.comp_on")), true, async () => { await _a.SetMemoryCompression(true); await LoadCompressionAsync(); });
            _compOff = _compression.AddButton(Get("card.switch_to", Get("perfp.comp_off")), false, async () => { await _a.SetMemoryCompression(false); await LoadCompressionAsync(); });
            AddCard(_compression);

            // One-click boost (1.0 feature, unchanged behavior: preview + confirmation + undo)
            var boost = new ActionCard(Glyphs.Rocket, Get("perfp.boost_title"), Get("perfp.boost_desc"));
            boost.SetFact(Get("card.recommended"), Get("card.preference"));
            boost.SetFact(Get("card.benefit"), Get("perfp.boost_benefit"));
            boost.SetFact(Get("card.side_effects"), Get("perfp.boost_side"));
            boost.SetFact(Get("card.requires"), Get("card.requires_user"));
            boost.SetFact(Get("card.undo"), Get("card.undo_session"));
            boost.AddButton(Get("perfp.boost_run"), false, async () => { await _a.RunBoost(); RefreshAll(); });
            AddCard(boost);

            VisibleChanged += async (s, e) => { if (Visible) { RefreshAll(); await LoadPlansAsync(); await LoadCompressionAsync(); } };
        }

        private sealed record PlanItem(string Guid, string Name, bool Active)
        {
            public override string ToString() => Active ? $"{Name} ({Get("perfp.active")})" : Name;
        }

        private async Task LoadPlansAsync()
        {
            try
            {
                var plans = await _a.LoadPowerPlans();
                _plans.Items.Clear();
                foreach (var (guid, name, active) in plans)
                {
                    var item = new PlanItem(guid, name, active);
                    _plans.Items.Add(item);
                    if (active) _plans.SelectedItem = item;
                }
                var activePlan = plans.FirstOrDefault(p => p.Active);
                _power.SetState(activePlan.Name != null ? Get("card.current", activePlan.Name) : Get("card.state_unknown"), TextRole.Accent);
            }
            catch (Exception ex)
            {
                Logger.Instance.Error("PerformancePage", "power plans", ex);
                _power.SetState(Get("card.state_unknown"), TextRole.Muted);
            }
        }

        private async Task LoadCompressionAsync()
        {
            var r = await _a.GetMemoryCompression();
            if (!r.Success)
            {
                _compression.SetState(Get("card.state_unknown"), TextRole.Muted);
                return;
            }
            var (enabled, source) = r.Data;
            string state = enabled ? Get("perfp.comp_on") : Get("perfp.comp_off");
            if (source == MemoryManager.CompressionSource.CompressionProcess)
                state += " · " + Get("perfp.comp_detected");
            _compression.SetState(Get("card.current", state), enabled ? TextRole.Success : TextRole.Accent);
            _compOn.Enabled = !enabled || source != MemoryManager.CompressionSource.MMAgent;
            _compOff.Enabled = enabled || source != MemoryManager.CompressionSource.MMAgent;
        }

        public void RefreshAll()
        {
            foreach (var c in Cards<SettingCard>()) c.RefreshState();
        }
    }

    // =====================================================================================
    // Privacy
    // =====================================================================================

    public sealed class PrivacyActions
    {
        public required SettingActions Settings { get; init; }
        public required Func<string?> GetAdvertisingId { get; init; }
        public required Func<Task> ResetAdvertisingId { get; init; }
        public required Func<Task> ClearActivityData { get; init; }
        public required IReadOnlyList<string> DnsNames { get; init; }
        public required Func<string> GetDnsStatus { get; init; }
        public required Func<int, Task> SetDns { get; init; }
        public required Func<(bool Active, string Status)> GetHostsStatus { get; init; }
        public required Func<Task> RemoveHostsBlock { get; init; }
        public required Action ShowHostsDownloadDisabled { get; init; }
        public required Action OpenWindowsSecurity { get; init; }
    }

    public sealed class PrivacyPage : CardPage
    {
        private readonly PrivacyActions _a;
        private readonly ActionCard _dns, _hosts;
        private SettingCard? _adCard;
        private readonly ThemedComboBox _dnsCombo = new() { Width = 260, Font = UiFonts.Regular(9.5f) };

        public PrivacyPage(PrivacyActions a)
        {
            _a = a;
            if (OrgManagement.IsManaged)
                AddNotice(Get("privp.managed_notice"), TextRole.Warning);

            foreach (var def in SettingCatalog.InGroup(SettingGroup.Privacy))
            {
                var card = CardFactory.Setting(def, a.Settings, RefreshAll);
                if (def == SettingCatalog.AdvertisingId)
                {
                    _adCard = card;
                    card.AddExtraAction(Get("privp.reset_ad"), async () => { await _a.ResetAdvertisingId(); RefreshAll(); });
                }
                if (def == SettingCatalog.ActivityHistory)
                    card.AddExtraAction(Get("privp.clear_activity"), async () => { await _a.ClearActivityData(); RefreshAll(); });
                AddCard(card);
            }

            // DNS
            _dns = new ActionCard(Glyphs.Globe, Get("privp.dns_title"), Get("privp.dns_desc"));
            _dns.SetFact(Get("card.recommended"), Get("card.preference"));
            _dns.SetFact(Get("card.benefit"), Get("privp.dns_benefit"));
            _dns.SetFact(Get("card.side_effects"), Get("privp.dns_side"));
            _dns.SetFact(Get("card.requires"), Get("card.requires_admin"));
            _dns.SetFact(Get("card.undo"), Get("privp.dns_undo"));
            foreach (var n in a.DnsNames) _dnsCombo.Items.Add(n);
            _dnsCombo.SelectedIndex = 0;
            _dnsCombo.Height = 28;
            _dns.SetExtra(_dnsCombo);
            _dns.AddButton(Get("privp.dns_apply"), false, async () => { await _a.SetDns(_dnsCombo.SelectedIndex); RefreshAll(); });
            AddCard(_dns);

            // Ad-block hosts (download disabled since Phase 2; removal of an old block still works)
            _hosts = new ActionCard(Glyphs.Privacy, Get("privp.hosts_title"), Get("privp.hosts_desc"));
            _hosts.SetFact(Get("card.recommended"), Get("privp.hosts_recommended"));
            _hosts.SetFact(Get("card.side_effects"), Get("privp.hosts_side"));
            _hosts.SetFact(Get("card.requires"), Get("card.requires_admin"));
            _hosts.SetFact(Get("card.undo"), Get("privp.hosts_undo"));
            _hosts.AddButton(Get("privp.hosts_remove"), false, async () => { await _a.RemoveHostsBlock(); RefreshAll(); });
            _hosts.AddButton(Get("privp.hosts_why_off"), false, _a.ShowHostsDownloadDisabled);
            AddCard(_hosts);

            // Windows Security (never changed by K-Win)
            var security = new ActionCard(Glyphs.Admin, Get("privp.security_title"), Get("privp.security_desc"));
            security.SetFact(Get("card.recommended"), Get("privp.security_recommended"));
            security.SetFact(Get("card.requires"), Get("card.requires_user"));
            security.AddButton(Get("privp.security_open"), true, _a.OpenWindowsSecurity);
            AddCard(security);

            VisibleChanged += (s, e) => { if (Visible) RefreshAll(); };
        }

        public void RefreshAll()
        {
            foreach (var c in Cards<SettingCard>()) c.RefreshState();

            if (_adCard != null)
            {
                string? id = _a.GetAdvertisingId();
                _adCard.SetFact(Get("privp.ad_current_id"), string.IsNullOrEmpty(id) ? Get("dash.not_available") : Loc.Ltr(id));
            }
            _dns.SetState(_a.GetDnsStatus(), TextRole.Accent);
            var (active, status) = _a.GetHostsStatus();
            _hosts.SetState(status, active ? TextRole.Warning : TextRole.Muted);
        }
    }

    // =====================================================================================
    // Tweaks (Tools > Tweaks)
    // =====================================================================================

    public sealed class TweaksPage : CardPage
    {
        public TweaksPage(SettingActions a)
        {
            foreach (var def in SettingCatalog.InGroup(SettingGroup.Tweaks))
                AddCard(CardFactory.Setting(def, a, RefreshAll));
            VisibleChanged += (s, e) => { if (Visible) RefreshAll(); };
        }

        public void RefreshAll()
        {
            foreach (var c in Cards<SettingCard>()) c.RefreshState();
        }
    }
}
