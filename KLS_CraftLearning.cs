using System;
using System.Reflection;
using HarmonyLib;

// KitsuneLearnedSkills — the "learn by crafting" half.
//
// The XML half (Config/buffs.xml) covers learn-by-USING for 16 skills
// (kills with weapons, block damage with tools, consuming food/medical).
// This DLL covers learn-by-CRAFTING for every crafting skill: each completed
// craft looks up the crafted item's (or block's) UnlockedBy property and
// adds progress to that skill. $kls_pace_craft crafted items = +1 level
// (the pace cvar is seeded by buffKLSPace in Config/buffs.xml).
//
// Source note: this file was reconstructed from the shipped 0.6.1 DLL via
// ILSpy decompilation — the original source predates this repo. Behavior is
// identical to the shipped assembly.

public class KitsuneLearnedSkillsInit : IModApi
{
    private static bool inited;

    public void InitMod(Mod _modInstance)
    {
        if (inited) return;
        inited = true;
        Log.Out("[KitsuneLearnedSkills] Applying craft-to-learn Harmony patches");
        new Harmony(GetType().ToString()).PatchAll(Assembly.GetExecutingAssembly());
    }
}

// EntityPlayerLocal.GiveExp(CraftCompleteData) runs once per completed craft
// on the crafting player's client — both for backpack crafts and workstation
// queue output. Postfix so vanilla XP handling is untouched.
[HarmonyPatch(typeof(EntityPlayerLocal))]
[HarmonyPatch("GiveExp")]
[HarmonyPatch(new Type[] { typeof(CraftCompleteData) })]
public class KLS_GiveExpCraftPatch
{
    public static void Postfix(EntityPlayerLocal __instance, CraftCompleteData data)
    {
        try
        {
            KLS_CraftLearning.OnCraftComplete(__instance, data);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] craft-to-learn failed: " + ex);
        }
    }
}

public static class KLS_CraftLearning
{
    private const string PaceVar = "$kls_pace_craft";
    private const float DefaultPace = 4f;
    private const string CounterPrefix = "$kls_craft_";

    public static void OnCraftComplete(EntityPlayerLocal player, CraftCompleteData data)
    {
        if (player == null || data == null) return;

        ItemStack crafted = data.CraftedItemStack;
        if (crafted == null || crafted.IsEmpty()) return;

        ItemValue itemValue = crafted.itemValue;
        if (itemValue == null) return;

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
                AwardIfCraftingSkill(player, skill, crafted.count);
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

    private static void AwardIfCraftingSkill(EntityPlayerLocal player, string progressionName, int craftedCount)
    {
        Progression progression = player.Progression;
        if (progression == null) return;

        ProgressionValue value = progression.GetProgressionValue(progressionName);
        if (value == null) return;

        ProgressionClass cls = value.ProgressionClass;
        // Type 6 = crafting skill. Never touch attributes/perks/books.
        if (cls == null || (int)cls.Type != 6 || value.Level >= cls.MaxLevel) return;

        float pace = GetPace(player);
        if (pace < 1f) pace = 1f;

        string counter = CounterPrefix + progressionName;
        float count = player.Buffs.HasCustomVar(counter) ? player.Buffs.GetCustomVar(counter) : 0f;
        count += (craftedCount <= 0) ? 1 : craftedCount;

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
            GameManager.ShowTooltip(player, "Crafting improved your " + skillName + " knowledge.", false, false, 0f);
        }
    }

    private static float GetPace(EntityPlayerLocal player)
    {
        if (player.Buffs != null && player.Buffs.HasCustomVar(PaceVar))
        {
            float pace = player.Buffs.GetCustomVar(PaceVar);
            if (pace > 0f) return pace;
        }
        return DefaultPace;
    }
}
