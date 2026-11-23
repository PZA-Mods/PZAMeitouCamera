// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// 0.6.1 (the user's first play, 2026-11-21): every blow on a bandit already lying down read as a new knockout (Meitou's VictimWasIncapacitated
// said false again), and each started its own close-up: 9+ in a row, the camera never came back. The gate lets one through per victim per
// time down, and spaces them out. Pure (no game types): the offline check and the fuzz harness drive it with times worked out by hand.
namespace PZA.Camera.Meitou;

public sealed class KillCamGate
{
    /// <summary>A victim counts as down this long after its knockout close-up, unless a later hit sees it standing (seconds).</summary>
    public const double DownFor = 60;
    /// <summary>The least time between two kill cam close-ups (a kill of a new victim aside), and between two limbs on one victim (seconds).</summary>
    public const double MinGap = 3, LimbGap = 10;
    public const int Cap = 1024;

    readonly Dictionary<long, double> _down = new(), _limb = new();
    readonly HashSet<long> _dead = new();
    double _last = double.NegativeInfinity;

    /// <summary>A hit saw the victim on its feet: its next knockout counts again.</summary>
    public void SeenStanding(long victim) => _down.Remove(victim);

    /// <summary>Whether a kill cam moment ("kill", "knockout", "limb") on this victim at time <paramref name="now"/> (seconds) may play;
    /// <paramref name="why"/> says why not.</summary>
    public bool Allow(long victim, string what, double now, out string why)
    {
        why = "";
        if (!double.IsFinite(now)) { why = "no clock"; return false; }
        if (_down.Count > Cap || _limb.Count > Cap || _dead.Count > Cap) { _down.Clear(); _limb.Clear(); _dead.Clear(); }
        if (_dead.Contains(victim)) { why = "already dead"; return false; }
        switch (what)
        {
            case "kill":
                _dead.Add(victim);   // a kill after a knockout still plays, once, whatever the gap
                _down.Remove(victim);
                break;
            case "knockout":
                if (_down.TryGetValue(victim, out double at) && now - at < DownFor) { why = "already down"; return false; }
                if (now - _last < MinGap) { why = "too soon after the last close-up"; return false; }
                _down[victim] = now;
                break;
            default:
                if (_down.TryGetValue(victim, out double d) && now - d < DownFor) { why = "already down"; return false; }
                if (_limb.TryGetValue(victim, out double l) && now - l < LimbGap) { why = "a limb close-up just played"; return false; }
                if (now - _last < MinGap) { why = "too soon after the last close-up"; return false; }
                _limb[victim] = now;
                break;
        }
        _last = now;
        return true;
    }

    public void Clear() { _down.Clear(); _limb.Clear(); _dead.Clear(); _last = double.NegativeInfinity; }
}
