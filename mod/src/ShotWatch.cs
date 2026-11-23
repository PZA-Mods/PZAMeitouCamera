// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// 0.5.0: what a landed hit did, for the shots (Meitou's public ICombatHitObserver: tick thread, serial, read only). Two uses:
//   - the running shot's attacker hits its target: a kill, a knockout or a severed limb cuts to the close-up (Shots.Impact), whoever the caller;
//   - "Kill cam shots" (setting, off by default): the same moments that make PZA's SlowMo's kill cam slow time (its own switches, read by
//     SlowLink: on, kills / knockouts / limbs, our own characters, how long) play the close-up for that moment.
// The reading of "down" and "severed" is PZA's SlowMo's kill cam's (a kill: alive -> dead; a knockout: standing -> down, not dead; a limb:
// more arms / legs stumped or crushed than after the last hit on that victim). Nothing here changes the game; the moment goes to the host
// thread through Driver.Queue.
using Meitou.Simulation;
using Meitou.Simulation.Bodies;
using Meitou.Simulation.Combat;

namespace PZA.Camera.Meitou;

public sealed class ShotWatch : ICombatHitObserver
{
    /// <summary>The selection and PZA's SlowMo's kill-cam switches, copied on the host thread each frame (tick thread reads them).</summary>
    public static volatile CharacterId[] Selected = [];
    public static volatile KillCamSwitches? KillCam;
    public sealed record KillCamSwitches(bool Kills, bool Knockouts, bool Limbs, bool Own, double Seconds);

    static readonly Dictionary<long, int> s_severed = new();
    /// <summary>0.6.1: one close-up per victim per time down (tick thread only).</summary>
    public static readonly KillCamGate Gate = new();
    static readonly System.Diagnostics.Stopwatch s_clock = System.Diagnostics.Stopwatch.StartNew();
    const int SeveredCap = 4096;
    static World? s_world;

    public enum What { None, Kill, Knockout, Limb }

    /// <summary>The pure judgement (the offline check and the fuzz harness call it): as PZA's SlowMo's kill cam.</summary>
    public static What Judge(bool wasAlive, bool wasIncapacitated, bool deadNow, bool downNow, int severedBefore, int severedNow, bool hitPartSevered,
        bool kills = true, bool knockouts = true, bool limbs = true)
    {
        if (!wasAlive) return What.None;
        if (kills && deadNow) return What.Kill;
        if (knockouts && !wasIncapacitated && downNow && !deadNow) return What.Knockout;
        if (limbs && (severedBefore >= 0 ? severedNow > severedBefore : hitPartSevered && severedNow > 0)) return What.Limb;
        return What.None;
    }

    public void HitResolved(World world, in CombatHit hit)
    {
        if (!ReferenceEquals(world, s_world)) { s_world = world; s_severed.Clear(); Gate.Clear(); }
        var watch = Shots.Watch;
        var kc = Driver.Settings.KillCamShots ? KillCam : null;
        bool onShot = watch is not null && hit.Attacker == watch.Attacker && hit.Victim == watch.Victim;
        bool forKillCam = kc is not null && hit.Attacker != hit.Victim && (IsSelected(hit.Attacker) || (kc.Own && IsSelected(hit.Victim)));
        if (!onShot && !forKillCam && watch is null && kc is null) return;   // nothing to watch: no medical reads at all

        var victim = !hit.Victim.IsNone && world.Characters.TryResolveNext(hit.Victim, out int slot) ? world.Characters.Cold(slot) : null;
        if (victim?.Medical is not { } m || m.Parts.Count <= 0) return;
        int n = Math.Min(m.Parts.Count, 16), sevNow = 0;
        for (int i = 0; i < n; i++)
            if (m.Parts[i].Limb is LimbState.Stump or LimbState.Crushed) sevNow++;
        long key = ((long)hit.Victim.Slot << 32) | (uint)hit.Victim.Generation;
        int sevBefore = s_severed.TryGetValue(key, out int b) ? b : -1;
        if (s_severed.Count >= SeveredCap && sevBefore < 0) s_severed.Clear();
        s_severed[key] = sevNow;
        bool partSevered = hit.Part >= 0 && hit.Part < n && m.Parts[hit.Part].Limb is LimbState.Stump or LimbState.Crushed;
        if (!onShot && !forKillCam) return;

        var what = onShot
            ? Judge(hit.VictimWasAlive, hit.VictimWasIncapacitated, m.Dead, m.Unconscious || m.Dead, sevBefore, sevNow, partSevered)
            : Judge(hit.VictimWasAlive, hit.VictimWasIncapacitated, m.Dead, m.Unconscious || m.Dead, sevBefore, sevNow, partSevered, kc!.Kills, kc.Knockouts, kc.Limbs);
        if (!m.Dead && !m.Unconscious) Gate.SeenStanding(key);
        if (what == What.None) return;
        var (att, vic) = (hit.Attacker, hit.Victim);
        string w = what.ToString().ToLowerInvariant();
        if (!Gate.Allow(key, w, s_clock.Elapsed.TotalSeconds, out string no))
        {
            Counters.Add("killcam.gate." + (no == "already down" ? "down" : no == "already dead" ? "dead" : "gap"));
            return;
        }
        double sec = kc?.Seconds ?? 1;
        Driver.Queue(() => Shots.KillCamMoment(att, vic, w, sec), "shot watch: " + w);
    }

    static bool IsSelected(CharacterId id)
    {
        if (id.IsNone) return false;
        foreach (var s in Selected) if (s == id) return true;
        return false;
    }

    /// <summary>Host thread, once a frame: the selection and the kill-cam switches for the tick thread (read every half second).</summary>
    public static void HostFrame(double now)
    {
        if (!Driver.Settings.KillCamShots) { if (KillCam is not null) KillCam = null; if (Selected.Length > 0) Selected = []; return; }
        try { Selected = [.. PzaUi_Selected()]; } catch { Selected = []; }
        if (now - s_lastRead < 0.5) return;
        s_lastRead = now;
        var k = SlowLink.KillCam();
        var next = k is { On: true } x ? new KillCamSwitches(x.Kills, x.Knockouts, x.Limbs, x.Own, x.Seconds) : null;
        if ((next is null) != (KillCam is null))
            Log.Line(next is null ? "kill cam shots: waiting for PZA's SlowMo's kill cam (not installed, or its kill cam is off)" : "kill cam shots: following PZA's SlowMo's kill cam");
        KillCam = next;
    }

    static double s_lastRead = -1;
    static IReadOnlyList<CharacterId> PzaUi_Selected() => PZA.MeitouUi.PzaUi.SelectedCharacters();
}
