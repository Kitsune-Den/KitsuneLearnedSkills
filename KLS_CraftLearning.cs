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
//        - EntityPlayerLocal.GiveExp(CraftCompleteData): workstation crafts
//          that finished while the station's window was CLOSED (the tile
//          entity queues them in CraftCompleteList and hands them over on
//          the next open).
//        - XUiC_RecipeStack.giveExp: the backpack crafting queue, AND any
//          workstation craft that finished while its window was open (the
//          UI runs the queue then and skips CraftCompleteList). 0.6.1
//          missed this one entirely — backpack crafts taught nothing.
//
//   2. Workstation skill (fixes the forge deadlock): every workstation-queue
//      completion ALSO credits craftingWorkstations, on both paths above
//      (the RecipeStack path checks that its window is a workstation's).
//      Campfires count, which is the bootstrap: the campfire is craftable at
//      level 1, and cooking on it teaches you your way to the forge (unlocks
//      at 4). $kls_pace_station completions = +1. 0.10.0 only hooked the
//      closed-window path, and counted a merged batch there as one.
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
//        pipe baton            -> craftingRobotics (0.11.0: the baton is
//                                 Robotics' level-1 item but carries no
//                                 UnlockedBy, and the junk sledge, the first
//                                 item that does, unlocks at 11)
//
//   6. Backpack skill (Pack Rat): XUiC_LootWindowGroup.OpenLooting runs on
//      the looting player's client; _firstTimeTouched is true only for the
//      first open of a fresh, non-player container, so each container counts
//      once and re-opening your own storage can't be farmed. Credits
//      craftingBackpack, whose first craftable (backpackSmall) unlocks at 5.
//      Not the onSelfOpenLootContainer XML trigger: it fires TWICE per fresh
//      container (server LootContainerOpened + client lock response).
//      $kls_pace_loot containers = +1. The Pack Rat magazine is also kept
//      in loot and traders as a safety net, like armor's.
//
//   7. Seeds from harvesting (0.11.0): GameUtils.HarvestOnAttack runs on the
//      harvesting player's client for every hit that can drop Harvest items
//      (crops are picked by hitting them; vanilla's farming challenges count
//      from the same place). One harvest can drop several items (crop, bonus
//      crop, Farmer-armor seed), so this counts the crop BLOCK instead: once
//      per "crops"-tagged block with Harvest drops that the hit finishes off.
//      Farm crops and wild crops both count; dead plants and snowberry bushes
//      (fibre only, no "crops" tag or no Harvest drops) do not. Farm plots
//      stay as the gateway craft. $kls_pace_harvest crops = +1.
//
//   8. Armor and Robotics from repairing (0.11.0): XUiC_RecipeStack.
//      outputStack raises QuestEventManager.RepairedItem on the repairing
//      player's client when a repair in the crafting queue finishes, and
//      nothing else raises it. Items tagged "armor" credit craftingArmor,
//      items tagged "roboticsSkill" (stun baton, junk sledge, junk turret,
//      junk drone) credit craftingRobotics. $kls_pace_repair repairs = +1.
//
// All pace cvars are seeded by buffKLSPace in Config/buffs.xml, whose numbers
// come from Settings.xml (see KLS_Settings.cs); the defaults below read the
// same settings and are only fallbacks in case the buff hasn't applied yet.

public class KitsuneLearnedSkillsInit : IModApi
{
    private static bool inited;

    public void InitMod(Mod _modInstance)
    {
        if (inited) return;
        inited = true;
        KLS_Settings.ModPath = _modInstance.Path;
        KLS_Settings.Load();
        Log.Out("[KitsuneLearnedSkills] Applying learn-by-doing Harmony patches (learning pace " + KLS_Settings.Preset + ", set in Settings.xml)");
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
            // This path only ever fires for workstation crafts: one completion
            // event per recipe run toward craftingWorkstations, regardless of
            // what was crafted. TileEntityWorkstation.AddCraftComplete MERGES
            // repeat crafts of the same item into one entry (stack count grows,
            // RecipeUsedCount stays 1), so 0.10.0 counted a whole closed-window
            // batch of forged iron as a single completion. Recover the run
            // count from the stack size instead.
            KLS_Award.AwardSkill(__instance, "craftingWorkstations", KLS_Award.RecipeRuns(data),
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

            // A workstation's queue runs HERE, not through the tile entity,
            // while its window is open: XUiC_RecipeStack.outputStack calls
            // giveExp directly for the player who started the craft and never
            // touches CraftCompleteList. 0.10.0 only credited Workstations on
            // the GiveExp(CraftCompleteData) path, so anyone who watched their
            // campfire or forge while it worked never levelled Workstations.
            if (__instance.windowGroup?.Controller is XUiC_WorkstationWindowGroup)
            {
                KLS_Award.AwardSkill(player, "craftingWorkstations", 1,
                    KLS_Award.StationPaceVar, KLS_Award.StationDefaultPace);
            }
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

// First open of a fresh loot container (see header item 6). Client-side;
// _firstTimeTouched is the server's "never opened, not player-owned" flag.
[HarmonyPatch(typeof(XUiC_LootWindowGroup))]
[HarmonyPatch("OpenLooting")]
public class KLS_LootOpenPatch
{
    public static void Postfix(XUiC_LootWindowGroup __instance, TEFeatureStorage _te, bool _firstTimeTouched)
    {
        try
        {
            if (!_firstTimeTouched || _te == null || _te.ItemGrid == null || _te.ItemGrid.PlayerOwned) return;
            EntityPlayerLocal player = __instance?.xui?.playerUI?.entityPlayer;
            if (player == null) return;
            KLS_Award.AwardSkill(player, "craftingBackpack", 1,
                KLS_Award.LootPaceVar, KLS_Award.LootDefaultPace);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] loot-to-learn failed: " + ex);
        }
    }
}

// Crop harvest (see header item 7). The prefix reads the block before vanilla
// clears attackDetails.blockBeingDamaged at the end; bKilled is left alone, so
// the postfix can tell the hit that finished the crop from the ones before it.
[HarmonyPatch(typeof(GameUtils))]
[HarmonyPatch("HarvestOnAttack")]
public class KLS_HarvestPatch
{
    private static readonly FastTags<TagGroup.Global> CropTag = FastTags<TagGroup.Global>.Parse("crops");

    public static void Prefix(ItemActionData _actionData, out bool __state)
    {
        __state = false;
        try
        {
            if (!(_actionData?.invData?.holdingEntity is EntityPlayerLocal)) return;
            ItemActionAttack.AttackHitInfo details = _actionData.attackDetails;
            if (details?.itemsToDrop == null || details.blockBeingDamaged.isair) return;
            if (!details.itemsToDrop.TryGetValue(EnumDropEvent.Harvest, out List<Block.SItemDropProb> drops)
                || drops == null || drops.Count == 0) return;
            Block block = details.blockBeingDamaged.Block;
            __state = block != null && block.HasAnyFastTags(CropTag);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] harvest-to-learn check failed: " + ex);
        }
    }

    public static void Postfix(ItemActionData _actionData, bool __state)
    {
        if (!__state) return;
        try
        {
            if (_actionData.attackDetails == null || !_actionData.attackDetails.bKilled) return;
            KLS_Award.AwardSkill((EntityPlayerLocal)_actionData.invData.holdingEntity, "craftingSeeds", 1,
                KLS_Award.HarvestPaceVar, KLS_Award.HarvestDefaultPace);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] harvest-to-learn failed: " + ex);
        }
    }
}

// Finished item repair from the crafting queue (see header item 8). Only
// XUiC_RecipeStack.outputStack raises this, on the repairing player's client.
[HarmonyPatch(typeof(QuestEventManager))]
[HarmonyPatch("RepairedItem")]
public class KLS_ItemRepairPatch
{
    private static readonly FastTags<TagGroup.Global> ArmorTag = FastTags<TagGroup.Global>.Parse("armor");
    private static readonly FastTags<TagGroup.Global> RoboticsTag = FastTags<TagGroup.Global>.Parse("roboticsSkill");

    public static void Postfix(ItemValue newValue)
    {
        try
        {
            ItemClass itemClass = newValue?.ItemClass;
            if (itemClass == null) return;
            string skill = itemClass.HasAnyTags(ArmorTag) ? "craftingArmor"
                : itemClass.HasAnyTags(RoboticsTag) ? "craftingRobotics"
                : null;
            if (skill == null) return;
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player == null) return;
            KLS_Award.AwardSkill(player, skill, 1, KLS_Award.RepairPaceVar, KLS_Award.RepairDefaultPace);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] repair-to-learn failed: " + ex);
        }
    }
}

public static class KLS_Award
{
    public const string CraftPaceVar = "$kls_pace_craft";
    public const string StationPaceVar = "$kls_pace_station";
    public const string VehiclePaceVar = "$kls_pace_vehicle";
    public const string WirePaceVar = "$kls_pace_wire";
    public const string LootPaceVar = "$kls_pace_loot";
    public const string HarvestPaceVar = "$kls_pace_harvest";
    public const string RepairPaceVar = "$kls_pace_repair";

    public static float CraftDefaultPace => KLS_Settings.Craft;
    public static float StationDefaultPace => KLS_Settings.Station;
    public static float VehicleDefaultPace => KLS_Settings.Vehicle;
    public static float WireDefaultPace => KLS_Settings.Wire;
    public static float LootDefaultPace => KLS_Settings.Loot;
    public static float HarvestDefaultPace => KLS_Settings.Harvest;
    public static float RepairDefaultPace => KLS_Settings.Repair;

    private const string CounterPrefix = "$kls_craft_";

    // Config/Localization.csv; {0} is the game's own localized skill name.
    private const string MessageKey = "klsMsgSkillGrows";

    // Always-unlocked recipes whose skill line has no UnlockedBy craftable at
    // level 1 — hard-mapped so the line can bootstrap. Keyed by recipe name.
    private static readonly Dictionary<string, string> GatewayRecipes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "trapSpikesWoodDmg0",        "craftingTraps" },
        { "trapSpikesIronDmg0",        "craftingTraps" },
        { "barbedFence",               "craftingTraps" },
        { "farmPlotBlockVariantHelper", "craftingSeeds" },
        { "meleeWpnBatonT0PipeBaton",   "craftingRobotics" },
    };

    // How many times the recipe ran for one CraftCompleteData. Merged entries
    // keep RecipeUsedCount at 1 while the stack grows, so divide the stack by
    // the recipe's output count and take whichever is larger.
    public static int RecipeRuns(CraftCompleteData data)
    {
        int runs = (data.RecipeUsedCount > 0) ? data.RecipeUsedCount : 1;
        ItemStack crafted = data.CraftedItemStack;
        if (crafted == null || crafted.IsEmpty() || string.IsNullOrEmpty(data.RecipeName)) return runs;
        try
        {
            Recipe recipe = CraftingManager.GetRecipe(data.RecipeName);
            int perRun = (recipe != null && recipe.count > 0) ? recipe.count : 1;
            int fromStack = crafted.count / perRun;
            if (fromStack > runs) runs = fromStack;
        }
        catch (Exception)
        {
        }
        return runs;
    }

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
            string format = Localization.Exists(MessageKey) ? Localization.Get(MessageKey, false, null) : "Your {0} knowledge grows.";
            GameManager.ShowTooltip(player, string.Format(format, skillName), false, false, 0f);
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
