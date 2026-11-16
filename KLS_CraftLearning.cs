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
//      0.12.0: a completion is weighed by the recipe's base craft time, so a
//      stack of cobblestone no longer teaches as much as a stack of guns
//      (see KLS_Award.StationWeight).
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
//   9. Exactly one level per XML level-up (0.13.0): buffs.xml levels skills
//      with vanilla's AddProgressionLevel level="1", and vanilla turns
//      level="1" on a crafting skill into the world's Magazine Progress
//      option (PointsPerMagazine), so a world set to 3 gave 3 levels per
//      threshold. A prefix runs buffKLSLearn's own AddProgressionLevel
//      actions without that swap. Magazines and every other mod's
//      AddProgressionLevel still go through vanilla.
//
//  10. Robotics from kills (0.14.0): a kill with a "roboticsSkill" item
//      (pipe baton, stun baton, junk sledge, junk turret) credits
//      craftingRobotics at $kls_pace_weapon, like every other weapon line.
//      Not the XML onSelfKilledOther pattern: a DEPLOYED turret or sledge
//      fires on the server (MiniTurretFireController.Fire), so on a dedicated
//      server that event never reaches the owner's client, and the owner is
//      holding something else anyway. Progression.AddLevelExp does run on the
//      killer's client for every kill, and carries the item that made it.
//
// Each DLL source keeps its own counter per skill ($kls_<source>_<skill>,
// e.g. $kls_harvest_craftingSeeds), measured against its own pace. Before
// 0.13.0 they all shared $kls_craft_<skill>, so a skill with two sources at
// different paces (Seeds: crafting 8, harvesting 24) levelled on whichever
// pace the last action checked against.
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
            float weight = KLS_Award.StationWeight(__instance, KLS_Award.FindRecipe(data.RecipeName));
            KLS_Award.AwardSkill(__instance, "craftingWorkstations", KLS_Award.RecipeRuns(data) * weight,
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
                Recipe baseRecipe = KLS_Award.FindRecipe(recipe.GetName(), recipe.craftingArea ?? "") ?? recipe;
                KLS_Award.AwardSkill(player, "craftingWorkstations", KLS_Award.StationWeight(player, baseRecipe),
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

// Kills with a robotics item (see header item 10). Both kill-XP paths end
// here on the killer's own client: EntityPlayer.AddKillXP directly when the
// killer is local to the machine that resolved the kill, and
// NetPackageEntityAddExpClient otherwise. Party-shared kills arrive as
// "_xpFromParty" with no item, so a party member's kills don't count.
[HarmonyPatch(typeof(Progression))]
[HarmonyPatch("AddLevelExp")]
public class KLS_RoboticsKillPatch
{
    private static readonly FastTags<TagGroup.Global> RoboticsTag = FastTags<TagGroup.Global>.Parse("roboticsSkill");

    public static void Postfix(Progression __instance, string _cvarXPName, Progression.XPTypes _xpType, ItemValue _itemValue)
    {
        try
        {
            if (_xpType != Progression.XPTypes.Kill || _cvarXPName != "_xpFromKill") return;
            ItemClass itemClass = _itemValue?.ItemClass;
            if (itemClass == null || !itemClass.HasAnyTags(RoboticsTag)) return;
            if (!(__instance?.parent is EntityPlayerLocal player)) return;
            KLS_Award.AwardSkill(player, "craftingRobotics", 1, KLS_Award.WeaponPaceVar, KLS_Award.WeaponDefaultPace);
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] robotics kill-to-learn failed: " + ex);
        }
    }
}

// buffKLSLearn's level-ups (see header item 9). Vanilla's Execute swaps
// level="1" for PointsPerMagazine on crafting skills; for our own actions,
// add the level the XML asks for instead. Anything else runs vanilla.
[HarmonyPatch(typeof(MinEventActionAddProgressionLevel))]
[HarmonyPatch("Execute")]
public class KLS_LearnLevelPatch
{
    public static bool Prefix(MinEventActionAddProgressionLevel __instance)
    {
        try
        {
            if (!KLS_LearnBuff.Owns(__instance)) return true;
            List<EntityAlive> targets = __instance.targets;
            if (targets == null) return false;
            for (int i = 0; i < targets.Count; i++)
            {
                EntityAlive target = targets[i];
                ProgressionValue value = target?.Progression?.GetProgressionValue(__instance.progressionName);
                if (value == null) continue;
                int oldLevel = value.Level;
                value.Level = Math.Max(0, Math.Min(oldLevel + __instance.level, value.ProgressionClass.MaxLevel));
                KLS_Award.Announce(target, value, oldLevel, __instance.showMessage);
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("[KitsuneLearnedSkills] learn-by-using level-up failed, falling back to vanilla: " + ex);
            return true;
        }
    }
}

// The AddProgressionLevel actions that belong to buffKLSLearn, found by
// reference in the loaded buff. Rebuilt whenever buffs.xml is reloaded (a new
// world, or joining a server that sends its own buffs.xml).
public static class KLS_LearnBuff
{
    private const string BuffName = "buffKLSLearn";

    private static BuffClass cachedFor;
    private static readonly HashSet<MinEventActionBase> actions = new HashSet<MinEventActionBase>();

    public static bool Owns(MinEventActionAddProgressionLevel action)
    {
        BuffClass buff = BuffManager.Buffs != null ? BuffManager.GetBuff(BuffName) : null;
        if (buff == null) return false;
        if (!ReferenceEquals(buff, cachedFor))
        {
            actions.Clear();
            List<MinEffectGroup> groups = buff.Effects?.EffectGroups;
            if (groups != null)
            {
                for (int i = 0; i < groups.Count; i++)
                {
                    foreach (List<MinEventActionBase> list in groups[i].TriggeredEffects.Values)
                    {
                        for (int j = 0; j < list.Count; j++)
                        {
                            if (list[j] is MinEventActionAddProgressionLevel) actions.Add(list[j]);
                        }
                    }
                }
            }
            cachedFor = buff;
        }
        return actions.Contains(action);
    }
}

public static class KLS_Award
{
    public const string WeaponPaceVar = "$kls_pace_weapon";
    public const string CraftPaceVar = "$kls_pace_craft";
    public const string StationPaceVar = "$kls_pace_station";
    public const string VehiclePaceVar = "$kls_pace_vehicle";
    public const string WirePaceVar = "$kls_pace_wire";
    public const string LootPaceVar = "$kls_pace_loot";
    public const string HarvestPaceVar = "$kls_pace_harvest";
    public const string RepairPaceVar = "$kls_pace_repair";
    public const string StationTimeVar = "$kls_pace_stationtime";
    public const string StationCapVar = "$kls_pace_stationcap";

    public static float WeaponDefaultPace => KLS_Settings.Weapon;
    public static float CraftDefaultPace => KLS_Settings.Craft;
    public static float StationDefaultPace => KLS_Settings.Station;
    public static float VehicleDefaultPace => KLS_Settings.Vehicle;
    public static float WireDefaultPace => KLS_Settings.Wire;
    public static float LootDefaultPace => KLS_Settings.Loot;
    public static float HarvestDefaultPace => KLS_Settings.Harvest;
    public static float RepairDefaultPace => KLS_Settings.Repair;

    private const string PacePrefix = "$kls_pace_";

    // Config/Localization.csv (V3.x) / .txt (V2.x); {0} is the game's own localized skill name.
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

    // How many completions one run of `recipe` counts as toward
    // craftingWorkstations. Before 0.12.0 every run counted as 1, so a base's
    // worth of cobblestone (0.4 s each) maxed the skill in an evening while a
    // pistol (53 s) counted the same as one cobblestone.
    //
    // The weight is the recipe's BASE craft time (Recipe.craftingTime: the
    // craft_time attribute, or vanilla's sum of the ingredients'
    // CraftingIngredientTime) divided by $kls_pace_stationtime, capped at
    // $kls_pace_stationcap. With the defaults (15 s, cap 4): cobblestone
    // 0.03, forged iron 0.5, boiled water 0.7, grilled meat 1, a pistol 3.5,
    // anything of a minute or more 4. The base time is used, not the time the
    // craft actually took, so perks and tools that speed up crafting don't
    // slow down learning. Instant recipes teach nothing.
    public static float StationWeight(EntityPlayerLocal player, Recipe recipe)
    {
        if (recipe == null) return 1f;

        float unit = GetPace(player, StationTimeVar, KLS_Settings.StationTime);
        float cap = GetPace(player, StationCapVar, KLS_Settings.StationCap);
        if (unit <= 0f || cap <= 0f) return 1f;

        float weight = recipe.craftingTime / unit;
        if (float.IsNaN(weight) || weight <= 0f) return 0f;
        return (weight > cap) ? cap : weight;
    }

    // The recipe as recipes.xml defines it. Neither queue path hands us that:
    // the UI queues a COPY whose craftingTime is already scaled by perks and
    // the sandbox crafting-time option (ItemActionEntryCraft), and a
    // CraftCompleteData only carries the recipe's name. Several recipes can
    // share a name (campfire and chem-station antibiotics), so prefer the one
    // for `craftingArea` when it is known, else take the first like vanilla.
    public static Recipe FindRecipe(string recipeName, string craftingArea = null)
    {
        if (string.IsNullOrEmpty(recipeName)) return null;
        try
        {
            List<Recipe> all = CraftingManager.GetAllRecipes();
            if (all == null) return null;
            Recipe first = null;
            for (int i = 0; i < all.Count; i++)
            {
                Recipe candidate = all[i];
                if (candidate == null || candidate.GetName() != recipeName) continue;
                if (craftingArea == null || string.Equals(candidate.craftingArea ?? "", craftingArea)) return candidate;
                if (first == null) first = candidate;
            }
            return first;
        }
        catch (Exception)
        {
            return null;
        }
    }

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

    // Add `amount` doing-events to `progressionName`'s counter for this source;
    // every `pace` events = +1 level. `amount` can be a fraction (weighted
    // workstation completions). Only ever touches crafting skills
    // (ProgressionClass Type 6) — attributes, perks and books are never modified.
    public static void AwardSkill(EntityPlayerLocal player, string progressionName, float amount,
        string paceVar, float defaultPace)
    {
        if (float.IsNaN(amount) || amount <= 0f) return;

        Progression progression = player.Progression;
        if (progression == null) return;

        ProgressionValue value = progression.GetProgressionValue(progressionName);
        if (value == null) return;

        ProgressionClass cls = value.ProgressionClass;
        if (cls == null || (int)cls.Type != 6 || value.Level >= cls.MaxLevel) return;

        float pace = GetPace(player, paceVar, defaultPace);
        if (pace < 1f) pace = 1f;

        string counter = CounterVar(paceVar, progressionName);
        float count = player.Buffs.HasCustomVar(counter) ? player.Buffs.GetCustomVar(counter) : 0f;
        count += amount;

        int oldLevel = value.Level;
        while (count >= pace && value.Level < cls.MaxLevel)
        {
            count -= pace;
            value.Level += 1;
        }
        player.Buffs.SetCustomVar(counter, count, true, (CVarOperation)0);

        if (value.Level > oldLevel)
        {
            Announce(player, value, oldLevel, true);
            string skillName = Localization.Get(cls.NameKey, false, null);
            if (string.IsNullOrEmpty(skillName)) skillName = progressionName;
            string format = Localization.Exists(MessageKey) ? Localization.Get(MessageKey, false, null) : "Your {0} knowledge grows.";
            GameManager.ShowTooltip(player, string.Format(format, skillName), false, false, 0f);
        }
    }

    // $kls_<source>_<skill>, where the source is the pace var's suffix
    // ($kls_pace_harvest -> $kls_harvest_craftingSeeds). Crafting keeps the
    // pre-0.13.0 name, $kls_craft_<skill>, so existing craft progress carries on.
    private static string CounterVar(string paceVar, string progressionName)
    {
        string source = paceVar.StartsWith(PacePrefix, StringComparison.Ordinal)
            ? paceVar.Substring(PacePrefix.Length)
            : "craft";
        return "$kls_" + source + "_" + progressionName;
    }

    // What vanilla's AddProgressionLevel does after changing a level: the
    // crafting-skill notification and new-recipe messages for the local
    // player, and the flag that sends progression to the server with the next
    // stats update (without it, the server kept the old level until something
    // else changed the player's progression).
    public static void Announce(EntityAlive target, ProgressionValue value, int oldLevel, bool showMessage)
    {
        if (value.Level != oldLevel && value.ProgressionClass.IsCrafting
            && target is EntityPlayerLocal local && XUiM_Recipes.CraftingProgression)
        {
            local.PlayerUI.xui.CollectedItemList.AddCraftingSkillNotification(value);
            if (showMessage) value.ProgressionClass.HandleCheckCrafting(local, oldLevel, value.Level);
        }
        target.Progression.bProgressionStatsChanged = !target.isEntityRemote;
        target.bPlayerStatsChanged |= !target.isEntityRemote;
    }

    public static float GetPace(EntityPlayerLocal player, string paceVar, float defaultPace)
    {
        if (player.Buffs != null && player.Buffs.HasCustomVar(paceVar))
        {
            float pace = player.Buffs.GetCustomVar(paceVar);
            if (pace > 0f) return pace;
        }
        return defaultPace;
    }
}

// "kls" console command (0.15.0): how far each crafting skill is toward its
// next level, per source. Every counter is a cvar on the player, so this only
// reads. It runs on the player's own client (IsExecuteOnClient), and the
// default permission level lets every player on a server use it.
public class KLS_ConsoleCmdProgress : ConsoleCmdAbstract
{
    // buffs.xml counters: skill, counter cvar, Settings.xml pace key.
    private static readonly string[][] XmlSources =
    {
        new[] { "craftingClubs", "$kls_clubs", "weapon" },
        new[] { "craftingBlades", "$kls_blades", "weapon" },
        new[] { "craftingSpears", "$kls_spears", "weapon" },
        new[] { "craftingSledgehammers", "$kls_sledges", "weapon" },
        new[] { "craftingKnuckles", "$kls_knuckles", "weapon" },
        new[] { "craftingBows", "$kls_bows", "weapon" },
        new[] { "craftingHandguns", "$kls_handguns", "weapon" },
        new[] { "craftingShotguns", "$kls_shotguns", "weapon" },
        new[] { "craftingRifles", "$kls_rifles", "weapon" },
        new[] { "craftingMachineGuns", "$kls_machineguns", "weapon" },
        new[] { "craftingExplosives", "$kls_explosives", "weapon" },
        new[] { "craftingHarvestingTools", "$kls_harvest", "tool" },
        new[] { "craftingSalvageTools", "$kls_salvage", "tool" },
        new[] { "craftingRepairTools", "$kls_repair", "tool" },
        new[] { "craftingFood", "$kls_food", "use" },
        new[] { "craftingMedical", "$kls_medical", "use" },
        new[] { "craftingVehicles", "$kls_wreck", "wreck" },
    };

    // DLL counters are $kls_<key>_<skill> (KLS_Award.CounterVar).
    private static readonly string[] DllSources = { "craft", "station", "vehicle", "wire", "loot", "harvest", "repair", "weapon" };

    private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
    {
        { "weapon", "kills" }, { "tool", "tool hits" }, { "use", "uses" }, { "wreck", "wreck hits" },
        { "craft", "crafts" }, { "station", "station work" }, { "vehicle", "vehicle repairs" },
        { "wire", "wire connections" }, { "loot", "containers" }, { "harvest", "crops" }, { "repair", "repairs" },
    };

    public override bool IsExecuteOnClient => true;
    public override int DefaultPermissionLevel => 1000;

    public override string[] getCommands()
    {
        return new[] { "kls", "learnedskills" };
    }

    public override string getDescription()
    {
        return "KitsuneLearnedSkills: shows progress toward the next level of each crafting skill";
    }

    public override string getHelp()
    {
        return "Usage:\n  kls          all crafting skills\n  kls <text>   only skills whose name contains <text>, e.g. kls work\n"
            + "Each source shows what you have counted so far, how many it takes per level, and the percentage.";
    }

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
        try
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            if (player?.Progression == null || player.Buffs == null)
            {
                console.Output("[KitsuneLearnedSkills] No local player. Run this in game, from your own console.");
                return;
            }

            string filter = (_params != null && _params.Count > 0) ? string.Join(" ", _params).Trim() : "";
            List<string> lines = new List<string>();
            foreach (ProgressionValue value in player.Progression.GetDict().Values)
            {
                ProgressionClass cls = value?.ProgressionClass;
                if (cls == null || !cls.IsCrafting) continue;

                string name = Localization.Get(cls.NameKey, false, null);
                if (string.IsNullOrEmpty(name)) name = cls.Name;
                if (filter.Length != 0
                    && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0
                    && cls.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                string line = name + " " + value.Level + "/" + cls.MaxLevel + ": ";
                if (value.Level >= cls.MaxLevel)
                {
                    lines.Add(line + "maxed");
                    continue;
                }

                List<string> parts = new List<string>();
                for (int i = 0; i < XmlSources.Length; i++)
                {
                    if (XmlSources[i][0] == cls.Name) AddPart(parts, player, XmlSources[i][1], XmlSources[i][2], true);
                }
                for (int i = 0; i < DllSources.Length; i++)
                {
                    AddPart(parts, player, "$kls_" + DllSources[i] + "_" + cls.Name, DllSources[i], false);
                }
                lines.Add(line + (parts.Count != 0 ? string.Join(", ", parts) : "nothing counted yet"));
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);
            console.Output("[KitsuneLearnedSkills] Progress toward the next level (counted / needed):");
            for (int i = 0; i < lines.Count; i++) console.Output("  " + lines[i]);
            if (lines.Count == 0) console.Output("  no crafting skill matches \"" + filter + "\"");
        }
        catch (Exception ex)
        {
            console.Output("[KitsuneLearnedSkills] kls failed: " + ex.Message);
            Log.Error("[KitsuneLearnedSkills] kls command failed: " + ex);
        }
    }

    // One "label counted/needed (percent)" entry. A source the skill always
    // has (XML) is listed even at zero; a DLL source only once it has counted.
    private static void AddPart(List<string> parts, EntityPlayerLocal player, string counter, string key, bool always)
    {
        bool has = player.Buffs.HasCustomVar(counter);
        if (!has && !always) return;

        float count = has ? player.Buffs.GetCustomVar(counter) : 0f;
        float pace = KLS_Award.GetPace(player, "$kls_pace_" + key, KLS_Settings.ByKey(key));
        if (pace < 1f) pace = 1f;
        if (count < 0f) count = 0f;

        int percent = (int)Math.Floor(Math.Min(count / pace, 1f) * 100f);
        parts.Add(Labels[key] + " " + count.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)
            + "/" + pace.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " (" + percent + "%)");
    }
}
