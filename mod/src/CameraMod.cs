// PZA's Camera for Meitou. Author: PZA. Licence: GPLv3 or later.
// The code mod's entry (meitou.json "entry"). Patches are applied inside Initialize, before the game's JIT warm-up (MeitouClient docs/modding.md
// "Harmony"). Anything missing -> the mod stays off with a line saying why; the game goes on.
using System.Reflection;
using Meitou.Localisation;
using Meitou.Modding;
using Meitou.Simulation;

namespace PZA.Camera.Meitou;

public sealed class CameraMod : IMod
{
    public void Initialize(ModContext context)
    {
        Log.Sink = context.Log;
        Driver.Group = context.AddSettings(Driver.Settings);   // saved values are applied before this returns
        // 0.2.2: Meitou keeps only the Settings page's own changes in meitou.user.json; what the window changes (places, opacity, the tiny
        // form, the SETTINGS rows, the toggles, the Low angle) is kept in pza.camera.settings.json in the mod's folder (PZA's Meitou UI 0.3.1)
        var kept = PZA.MeitouUi.PzaSettings.Keep(Driver.Group, context.Folder);
        foreach (var line in kept) Log.Line(line);

        string version = typeof(CameraMod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        int plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];
        Log.Line($"{version} for Meitou, by PZA: Shift+K the window, Shift+J saves the Low angle; camera at load: {(Driver.Settings.StartOn ? "Diorama" : "off")}");

        foreach (var line in Strings.Init(context.Folder, Loc.Language?.Code ?? "en", Driver.Settings.Language)) Log.Line(line);

        if (!Start(context.Harmony)) return;
        PZA.MeitouUi.PzaUi.Log = s => Log.Line(s);
        PZA.MeitouUi.PzaUi.EachFrame = CamWindow.EachFrame;
        CamWindow.Setup();
        PZA.MeitouUi.PzaUi.Install(context);
        Driver.Ready = true;
        // 0.5.0: the shots watch landed hits (the running shot's impact; the kill cam shots)
        context.AddSystem("pza.camera.shotwatch", _ => new ShotWatch(), SystemAnchor.End);
        Log.Line($"called-shot shots {(Driver.Settings.Shots ? "on" : "off")} (CameraApi version {CameraApi.Version}); kill cam shots {(Driver.Settings.KillCamShots ? "on" : "off")}");
        context.AddDebugCommand("PZA's Camera: next camera", _ => Driver.DebugNext());
        context.AddDebugCommand("PZA's Camera: preview a called shot", _ => { Driver.Queue(() => Shots.Preview("F10"), "F10 preview"); return "PZA's Camera: preview"; });
        context.AddDebugCommand("PZA's Camera: window (Shift+K)", _ => { Driver.Queue(CamWindow.HotkeyToggle, "F10"); return "PZA's Camera window"; });
    }

    /// <summary>Resolves the game's members and patches; false (with a line) when anything is missing. Public for the offline check.</summary>
    public static bool Start(HarmonyLib.Harmony harmony)
    {
        var missing = Access.ResolveAll();
        if (missing.Count > 0)
        {
            Log.Line("stays OFF: the game lacks " + string.Join("; ", missing) + " (built for Meitou 0.20.1)");
            return false;
        }
        try
        {
            foreach (var line in Access.ResolveOptional()) Log.Line(line);
            foreach (var line in Patches.Apply(harmony)) Log.Line(line);
            if (Access.HasDof)
            {
                Shots.Dof = (() => Driver.Host is { } h ? Access.PhotoDof(h) : null, v => { if (Driver.Host is { } h) Access.SetPhotoDof(h, v); });
                Shots.CinematicView = () => { if (Driver.Host is { } h) Access.SetCinematicView(h); };
            }
            return true;
        }
        catch (Exception e)
        {
            Log.Line("stays OFF: patching failed (" + e.GetType().Name + ": " + e.Message + ")");
            return false;
        }
    }
}
