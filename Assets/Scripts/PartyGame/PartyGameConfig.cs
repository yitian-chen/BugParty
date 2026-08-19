using UnityEngine;

namespace PartyGame
{
    [CreateAssetMenu(fileName = "PartyGameConfig", menuName = "PartyGame/PartyGameConfig")]
    public class PartyGameConfig : ScriptableObject
    {
        [Header("Match Timing")]
        public float matchDuration = 180f;
        public float countdownToStart = 3f;
        public float waveInterval = 30f;
        public int totalWaves = 6;
        public float frenzyStartTime = 150f;

        [Header("Wave Contents")]
        [Tooltip("Number of common fishing spots spawned per wave (waves 1-5).")]
        public int commonSpotsPerWave = 3;
        [Tooltip("Fish count inside each common fishing spot.")]
        public int fishPerCommonSpot = 3;
        [Tooltip("Radius of the trigger volume of common spots (meters).")]
        public float commonSpotRadius = 2f;
        [Tooltip("Radius of the trigger volume of the golden spot (meters).")]
        public float goldenSpotRadius = 3f;

        [Header("Player / Raft")]
        public float playerMoveSpeed = 6f;
        [Tooltip("Forward acceleration (m/s^2) while pushing forward or reverse. Lower = more inertia when starting from a stop.")]
        public float playerAccel = 12f;
        [Tooltip("Deceleration (m/s^2) when input is released or reversed. Lower = more drift.")]
        public float playerDecel = 6f;
        [Tooltip("Yaw acceleration (deg/s^2). Lower = the raft resists sudden hard turns.")]
        public float playerTurnAccel = 360f;
        public int raftFishCapacity = 2;
        public int itemSlotCount = 2;

        [Header("Fishing")]
        public float largeNetDuration = 5f;
        public int largeNetFishAmount = 2;
        public float smallNetDuration = 5f;
        public int smallNetFishAmount = 1;
        public float bareHandDuration = 8f;
        public int bareHandFishAmount = 1;

        [Header("Items - Durability")]
        public int smallNetDurability = 3;
        public int largeNetDurability = 3;
        public int knifeDurability = 3;

        [Header("Combat")]
        public float knifeRange = 1.5f;
        public float mineStunDuration = 5f;
        public float mineTriggerRadius = 1f;
        [Tooltip("Prefab used when a player places a Mine. Must have a Mine component.")]
        public GameObject minePrefabRef;

        [Header("Water Gun (default sidearm)")]
        [Tooltip("Effective shot range in meters (short-range).")]
        public float waterGunRange = 8f;
        [Tooltip("Cylinder cast radius to be forgiving about aim precision.")]
        public float waterGunHitRadius = 0.7f;
        [Tooltip("Clip capacity.")]
        public int waterGunClipSize = 5;
        [Tooltip("Seconds needed to fully reload from empty (or from any non-full state).")]
        public float waterGunReloadSeconds = 4f;
        [Tooltip("Minimum seconds between consecutive shots.")]
        public float waterGunFireCooldown = 0.25f;
        [Tooltip("Meters the victim is pushed backward on hit.")]
        public float waterGunKnockbackDistance = 1.5f;
        [Tooltip("Seconds the victim is slowed after being hit.")]
        public float waterGunSlowDuration = 1f;
        [Tooltip("Movement speed multiplier while slowed (0.4 = 60% slower).")]
        public float waterGunSlowMultiplier = 0.4f;
        [Tooltip("Seconds the victim is stunned after being hit (applied together with knockback).")]
        public float waterGunStunDuration = 1f;
        [Tooltip("Prefab to instantiate above the player's head while reloading. Reuse the fishing progress bar prefab for visual consistency; the runtime WaterReloadBar drives the FillPivot child.")]
        public GameObject reloadBarPrefab;

        [Header("Hook (grappling hook item)")]
        [Tooltip("Maximum hook travel range in meters. If it reaches the end without hitting anything the shot fails.")]
        public float hookRange = 40f;
        [Tooltip("Casting speed of the hook line visual (m/s). Bigger = the rope grows out faster; server resolves the hit instantly, this only tunes the visual.")]
        public float hookCastSpeed = 18f;
        [Tooltip("Minimum seconds between consecutive hook shots.")]
        public float hookCooldown = 4f;
        [Tooltip("Total number of hook shots granted per pickup — item is consumed when durability hits 0.")]
        public int hookDurability = 5;
        [Tooltip("How far in front of the caster (meters) a hooked player is placed after the pull.")]
        public float hookPullTargetDistance = 2.0f;
        [Tooltip("Cylinder cast radius for forgiving hooked-target detection.")]
        public float hookHitRadius = 1.2f;
        [Tooltip("Seconds a hooked victim smoothly lerps toward the drop position (owner-side).")]
        public float hookPullDuration = 0.7f;

        [Header("Item Registry (for network kind -> SO lookup)")]
        [Tooltip("All ItemDataSO used in the match. Clients look up SOs by ItemKind through this list.")]
        public ItemDataSO[] allItems;

        public ItemDataSO GetItemByKind(ItemKind kind)
        {
            if (allItems == null) return null;
            foreach (var it in allItems) if (it != null && it.kind == kind) return it;
            return null;
        }

        [Header("Scoring")]
        public int commonFishScore = 1;
        public int goldenFishScore = 2;

        [Header("Frenzy")]
        public float frenzyMoveMultiplier = 2f;
        public float frenzyFishingSpeedMultiplier = 2f;

        [Header("Map Bounds")]
        [Tooltip("World-space half-extents (X and Z) of the play area (spots spawn within these bounds).")]
        public Vector2 mapHalfExtents = new Vector2(20f, 15f);
        [Tooltip("Minimum distance a common spot must keep from any island center.")]
        public float commonSpotMinDistanceFromIsland = 4f;
    }
}
