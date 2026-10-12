using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;

// Learning-speed settings, read from Settings.xml in the mod folder root.
//
// Before 0.8.0 the pace was picked by uncommenting one preset block inside
// Config/buffs.xml. Now Settings.xml names a preset (SLOW / MEDIUM / FAST)
// and can override any single number, and the DLL writes the result into
// buffKLSPace while the HOST builds buffs.xml:
//
//   WorldStaticData.loadSingleXml runs LoadAndPatchConfig (mod patches), then
//   ApplyConditionalXmlBlocks(Host), then compresses the XML for clients.
//   Prefixing the Host pass means a dedicated server's Settings.xml is baked
//   into the buffs.xml every client receives — clients' own copies of the
//   file never matter in multiplayer, so nobody can speed up their own pace.
//
// Settings.xml is re-read on every XML load, so in single player a change
// takes effect on the next world load without restarting the game. On a
// dedicated server, restart the server.
public static class KLS_Settings
{
    public const string FileName = "Settings.xml";
    public const string DefaultPreset = "SLOW";

    // Order matches the cvars buffKLSPace seeds; keys are the Settings.xml
    // attribute names.
    private static readonly string[] Keys = { "weapon", "tool", "use", "craft", "station", "vehicle", "wire" };

    private static readonly Dictionary<string, float[]> Presets =
        new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase)
    {
        //                       weapon tool use craft station vehicle wire
        { "SLOW",   new float[] { 16f, 80f, 8f, 8f, 16f, 4f, 4f } },
        { "MEDIUM", new float[] { 12f, 60f, 6f, 6f,  8f, 2f, 2f } },
        { "FAST",   new float[] {  4f, 20f, 2f, 2f,  4f, 1f, 1f } },
    };

    public static string ModPath;

    public static string Preset { get; private set; } = DefaultPreset;
    private static float[] values = (float[])Presets[DefaultPreset].Clone();

    public static float Weapon => values[0];
    public static float Tool => values[1];
    public static float Use => values[2];
    public static float Craft => values[3];
    public static float Station => values[4];
    public static float Vehicle => values[5];
    public static float Wire => values[6];

    public static void Load()
    {
        Preset = DefaultPreset;
        values = (float[])Presets[DefaultPreset].Clone();
        try
        {
            if (string.IsNullOrEmpty(ModPath)) return;

            string path = Path.Combine(ModPath, FileName);
            if (!File.Exists(path))
            {
                Log.Out("[KitsuneLearnedSkills] No " + FileName + " found; using the " + DefaultPreset + " learning pace.");
                return;
            }

            XElement root = XDocument.Load(path).Root;
            if (root == null) return;

            string preset = ((string)root.Attribute("preset") ?? "").Trim();
            if (preset.Length != 0)
            {
                if (Presets.TryGetValue(preset, out float[] presetValues))
                {
                    Preset = preset.ToUpperInvariant();
                    values = (float[])presetValues.Clone();
                }
                else
                {
                    Log.Warning("[KitsuneLearnedSkills] " + FileName + ": unknown preset '" + preset
                        + "' (use SLOW, MEDIUM or FAST); using " + DefaultPreset + ".");
                }
            }

            // Any number set explicitly overrides the preset's value for that source.
            bool overridden = false;
            for (int i = 0; i < Keys.Length; i++)
            {
                string raw = ((string)root.Attribute(Keys[i]) ?? "").Trim();
                if (raw.Length == 0) continue;
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && v >= 1f)
                {
                    values[i] = v;
                    overridden = true;
                }
                else
                {
                    Log.Warning("[KitsuneLearnedSkills] " + FileName + ": " + Keys[i] + "=\"" + raw
                        + "\" is not a number of 1 or more; keeping " + Format(values[i]) + ".");
                }
            }
            if (overridden) Preset += " (custom)";
        }
        catch (Exception ex)
        {
            Preset = DefaultPreset;
            values = (float[])Presets[DefaultPreset].Clone();
            Log.Warning("[KitsuneLearnedSkills] Failed to read " + FileName + " (" + ex.Message + "); using the "
                + DefaultPreset + " learning pace.");
        }
    }

    // Replace buffKLSPace's effect groups with one built from the settings.
    public static void ApplyToBuffs(XDocument doc)
    {
        XElement buff = doc?.Root?.Elements("buff")
            .FirstOrDefault(b => (string)b.Attribute("name") == "buffKLSPace");
        if (buff == null)
        {
            Log.Warning("[KitsuneLearnedSkills] buffKLSPace not found in buffs.xml; " + FileName + " not applied.");
            return;
        }

        buff.Elements("effect_group").Remove();
        XElement group = new XElement("effect_group");
        for (int i = 0; i < Keys.Length; i++)
        {
            string cvar = "$kls_pace_" + Keys[i];
            string value = Format(values[i]);
            // Both triggers, same as the old XML presets: onSelfBuffStart for
            // new characters, onSelfEnteredGame so existing characters pick up
            // a changed pace on their next login.
            group.Add(ModifyCVar("onSelfBuffStart", cvar, value));
            group.Add(ModifyCVar("onSelfEnteredGame", cvar, value));
        }
        buff.Add(group);

        Log.Out("[KitsuneLearnedSkills] Learning pace " + Preset + ": " + string.Join(", ",
            Keys.Select((k, i) => k + " " + Format(values[i]))));
    }

    private static XElement ModifyCVar(string trigger, string cvar, string value)
    {
        return new XElement("triggered_effect",
            new XAttribute("trigger", trigger),
            new XAttribute("action", "ModifyCVar"),
            new XAttribute("cvar", cvar),
            new XAttribute("operation", "set"),
            new XAttribute("value", value));
    }

    private static string Format(float v)
    {
        return v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

// Host-side pass over each config XML, after every mod's patches are applied
// and before the result is cached for clients (see the header above). The
// original is an iterator, so this prefix runs when the coroutine is created,
// before the conditional blocks are processed. Clients applying configs
// received from a server only ever run the Client evaluator, so they skip this.
[HarmonyPatch(typeof(XmlPatcher))]
[HarmonyPatch("ApplyConditionalXmlBlocks")]
public class KLS_BuffsXmlPacePatch
{
    public static void Prefix(string _xmlName, XmlFile _xmlFile, XmlPatcher.EEvaluator _evaluator)
    {
        try
        {
            if (_evaluator != XmlPatcher.EEvaluator.Host || _xmlFile?.XmlDoc == null) return;
            if (!string.Equals(_xmlName, "buffs", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_xmlName, "buffs.xml", StringComparison.OrdinalIgnoreCase)) return;

            KLS_Settings.Load();
            KLS_Settings.ApplyToBuffs(_xmlFile.XmlDoc);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] applying " + KLS_Settings.FileName + " to buffs.xml failed: " + ex);
        }
    }
}
