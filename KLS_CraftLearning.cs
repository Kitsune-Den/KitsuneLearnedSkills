using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

// KitsuneLearnedSkills — the "learn by doing" DLL half.
//
// The XML half (Config/buffs.xml) covers learn-by-USING for 16 skills
// (kills with weapons, block damage with tools, consuming food/medical).
// This DLL covers everything the XML can't reach:
//
//   1. Learn by crafting (all skills): each completed craft reads the
//      crafted item's (or block's) UnlockedBy property and adds progress to
//      that skill. $kls_pace_craft crafted items = +1 level.
//      Two entry points, mutually exclusive by vanilla design:
//        - EntityPlayerLocal.GiveExp(CraftCompleteData): workstation queue
//          output (TileEntityWorkstation always routes through its
//          CraftCompleteList, even with the window open).
//        - XUiC_RecipeStack.giveExp: the backpack crafting queue. 0.6.1
//          missed this one entirely — backpack crafts taught nothing.
//
//   2. Workstation skill (fixes the forge deadlock): every workstation-queue
//      completion ALSO credits craftingWorkstations — the GiveExp(
//      CraftCompleteData) path only ever fires for workstation crafts, so no
//      area lookup is needed. Campfires count, which is the bootstrap: the
//      campfire is craftable at level 1, and cooking on it teaches you your
//      way to the forge (unlocks at 4). $kls_pace_station completions = +1.
//
//   3. Vehicle skill: XUiM_Vehicle.RepairVehicle (the repair button in the
//      vehicle UI, client-side, consumes a repair kit) credits
//      craftingVehicles on success. Repairing a looted bicycle is the
//      bootstrap below the first part unlock at 5. $kls_pace_vehicle = +1.
//
//   4. Electrician skill: TileEntityPowered.SetParentWithWireTool runs on
//      the wiring player's client once per completed wire connection.
//      A looted/bought wire tool is the bootstrap below the first unlock
//      at 25. $kls_pace_wire connections = +1.
//
//   5. Gateway crafts (always-unlocked recipes hard-mapped to a skill,
//      because their line's first UnlockedBy craftable sits above level 1):
//        spikes + barbed fence -> craftingTraps   (dart traps unlock at 25)
//        farm plots            -> craftingSeeds   (first seeds unlock at 2)
//
// All pace cvars are seeded by buffKLSPace in Config/buffs.xml (SLOW /
// MEDIUM / FAST presets); the constants below are only fallbacks in case the
// buff hasn't applied yet.

public class KitsuneLearnedSkillsInit : IModApi
{
    private static bool inited;

    public void InitMod(Mod _modInstance)
    {
        if (inited) return;
        inited = true;
        Log.Out("[KitsuneLearnedSkills] Applying learn-by-doing Harmony patches");
        new Harmony(GetType().ToString()).PatchAll(Assembly.GetExecutingAssembly());
    }
}

// Workstation queue output. TileEntityWorkstation.HandleTick always funnels
// completed crafts into CraftCompleteList, delivered here when the crafter
// accesses the station. Postfix so vanilla XP handling is untouched.
[HarmonyPatch(typeof(EntityPlayerLocal))]
[HarmonyPatch("GiveExp")]
[HarmonyPatch(new Type[] { typeof(CraftCompleteData) })]
public class KLS_GiveExpCraftPatch
{
    public static void Postfix(EntityPlayerLocal __instance, CraftCompleteData data)
    {
        try
        {
            if (__instance == null || data == null) return;
            KLS_Award.OnCraftComplete(__instance, data.CraftedItemStack, data.RecipeName);
            // This path only ever fires for workstation crafts: +1 completion
            // event (RecipeUsedCount, constructed as 1 per completion) toward
            // craftingWorkstations regardless of what was crafted.
            int events = (data.RecipeUsedCount > 0) ? data.RecipeUsedCount : 1;
            KLS_Award.AwardSkill(__instance, "craftingWorkstations", events,
                KLS_Award.StationPaceVar, KLS_Award.StationDefaultPace);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] craft-to-learn (workstation) failed: " + ex);
        }
    }
}

// Backpack crafting queue. Runs once per completed recipe execution on the
// crafting player's client.
[HarmonyPatch(typeof(XUiC_RecipeStack))]
[HarmonyPatch("giveExp")]
public class KLS_RecipeStackCraftPatch
{
    public static void Postfix(XUiC_RecipeStack __instance)
    {
        try
        {
            if (__instance == null) return;
            Recipe recipe = __instance.GetRecipe();
            if (recipe == null) return;
            EntityPlayerLocal player = __instance.xui?.playerUI?.entityPlayer;
            if (player == null) return;

            ItemValue itemValue = new ItemValue(recipe.itemValueType);
            ItemStack crafted = new ItemStack(itemValue, recipe.count);
            KLS_Award.OnCraftComplete(player, crafted, recipe.GetName());
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] craft-to-learn (backpack) failed: " + ex);
        }
    }
}

// Vehicle repair button (vehicle UI, consumes a repair kit). Client-side,
// __result is true when a repair actually happened.
[HarmonyPatch(typeof(XUiM_Vehicle))]
[HarmonyPatch("RepairVehicle")]
public class KLS_VehicleRepairPatch
{
    public static void Postfix(bool __result, XUi _xui)
    {
        try
        {
            if (!__result) return;
            EntityPlayerLocal player = _xui?.playerUI?.entityPlayer;
            if (player == null) return;
            KLS_Award.AwardSkill(player, "craftingVehicles", 1,
                KLS_Award.VehiclePaceVar, KLS_Award.VehicleDefaultPace);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] repair-to-learn failed: " + ex);
        }
    }
}

// Completed wire connection. ItemActionConnectPower calls this on the wiring
// player's client; on dedicated servers it may also run server-side where
// there is no primary player — the identity check filters that out.
[HarmonyPatch(typeof(TileEntityPowered))]
[HarmonyPatch("SetParentWithWireTool")]
public class KLS_WireToolPatch
{
    public static void Postfix(int wiringEntityID)
    {
        try
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null || player.entityId != wiringEntityID) return;
            KLS_Award.AwardSkill(player, "craftingElectrician", 1,
                KLS_Award.WirePaceVar, KLS_Award.WireDefaultPace);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] wire-to-learn failed: " + ex);
        }
    }
}

public static class KLS_Award
{
    public const string CraftPaceVar = "$kls_pace_craft";
    public const string StationPaceVar = "$kls_pace_station";
    public const string VehiclePaceVar = "$kls_pace_vehicle";
    public const string WirePaceVar = "$kls_pace_wire";

    public const float CraftDefaultPace = 4f;
    public const float StationDefaultPace = 8f;
    public const float VehicleDefaultPace = 2f;
    public const float WireDefaultPace = 2f;

    private const string CounterPrefix = "$kls_craft_";

    // Always-unlocked recipes whose skill line has no UnlockedBy craftable at
    // level 1 — hard-mapped so the line can bootstrap. Keyed by recipe name.
    private static readonly Dictionary<string, string> GatewayRecipes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "trapSpikesWoodDmg0",        "craftingTraps" },
        { "trapSpikesIronDmg0",        "craftingTraps" },
        { "barbedFence",               "craftingTraps" },
        { "farmPlotBlockVariantHelper", "craftingSeeds" },
    };

    // Shared craft handler for both queue paths: UnlockedBy award + gateway map.
    public static void OnCraftComplete(EntityPlayerLocal player, ItemStack crafted, string recipeName)
    {
        if (player == null || crafted == null || crafted.IsEmpty()) return;

        ItemValue itemValue = crafted.itemValue;
        if (itemValue == null) return;

        int count = (crafted.count <= 0) ? 1 : crafted.count;

        if (!string.IsNullOrEmpty(recipeName) && GatewayRecipes.TryGetValue(recipeName, out string gatewaySkill))
        {
            AwardSkill(player, gatewaySkill, count, CraftPaceVar, CraftDefaultPace);
        }

        DynamicProperties props = GetUnlockProperties(itemValue);
        if (props == null) return;

        string unlockedBy = props.GetString("UnlockedBy");
        if (string.IsNullOrEmpty(unlockedBy)) return;

        string[] skills = unlockedBy.Split(',');
        for (int i = 0; i < skills.Length; i++)
        {
            string skill = skills[i].Trim();
            if (skill.Length != 0)
            {
                AwardSkill(player, skill, count, CraftPaceVar, CraftDefaultPace);
            }
        }
    }

    // UnlockedBy usually lives on the ItemClass; seeds and traps exist only
    // as BLOCKS (plantedCotton1, dartTrap, autoTurret) and carry it on the
    // block instead, so fall back to the block properties.
    private static DynamicProperties GetUnlockProperties(ItemValue itemValue)
    {
        ItemClass itemClass = itemValue.ItemClass;
        if (itemClass != null && itemClass.Properties != null && itemClass.Properties.Contains("UnlockedBy"))
        {
            return itemClass.Properties;
        }
        try
        {
            int type = itemValue.type;
            if (type > 0 && Block.list != null && type < Block.list.Length)
            {
                Block block = Block.list[type];
                if (block != null && block.Properties != null && block.Properties.Contains("UnlockedBy"))
                {
                    return block.Properties;
                }
            }
        }
        catch (Exception)
        {
        }
        return null;
    }

    // Add `amount` doing-events to `progressionName`'s counter; every `pace`
    // events = +1 level. Only ever touches crafting skills (ProgressionClass
    // Type 6) — attributes, perks and books are never modified.
    public static void AwardSkill(EntityPlayerLocal player, string progressionName, int amount,
        string paceVar, float defaultPace)
    {
        Progression progression = player.Progression;
        if (progression == null) return;

        ProgressionValue value = progression.GetProgressionValue(progressionName);
        if (value == null) return;

        ProgressionClass cls = value.ProgressionClass;
        if (cls == null || (int)cls.Type != 6 || value.Level >= cls.MaxLevel) return;

        float pace = GetPace(player, paceVar, defaultPace);
        if (pace < 1f) pace = 1f;

        string counter = CounterPrefix + progressionName;
        float count = player.Buffs.HasCustomVar(counter) ? player.Buffs.GetCustomVar(counter) : 0f;
        count += (amount <= 0) ? 1 : amount;

        int gained = 0;
        while (count >= pace && value.Level < cls.MaxLevel)
        {
            count -= pace;
            value.Level += 1;
            gained++;
        }
        player.Buffs.SetCustomVar(counter, count, true, (CVarOperation)0);

        if (gained > 0)
        {
            string skillName = Localization.Get(cls.NameKey, false, null);
            if (string.IsNullOrEmpty(skillName)) skillName = progressionName;
            GameManager.ShowTooltip(player, "Your " + skillName + " knowledge grows.", false, false, 0f);
        }
    }

    private static float GetPace(EntityPlayerLocal player, string paceVar, float defaultPace)
    {
        if (player.Buffs != null && player.Buffs.HasCustomVar(paceVar))
        {
            float pace = player.Buffs.GetCustomVar(paceVar);
            if (pace > 0f) return pace;
        }
        return defaultPace;
    }
}
