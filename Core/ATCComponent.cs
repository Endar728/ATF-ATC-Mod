using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using NO_ATC_Mod.UI;
using NO_ATC_Mod.Core;

namespace NO_ATC_Mod.Core
{
    public static class ATCComponent
    {
        private static bool initialized = false;
        private static ATCWindow atcWindow;
        private static RadarSystem radarSystem;
        private static UI.MapOverlay mapOverlay;
        private static float lastUpdateTime = 0f;

        [HarmonyPatch(typeof(MainMenu), "Start")]
        public static class OnPlatformStart
        {
            public static void Postfix()
            {
                if (!initialized && Plugin.Enabled.Value)
                {
                    Plugin.Log.LogInfo("[ATC] Initializing ATC Component...");
                    
                    try
                    {
                        radarSystem = new RadarSystem();
                        
                        GameObject atcWindowObj = new GameObject("NO_ATC_Window");
                        atcWindow = atcWindowObj.AddComponent<UI.ATCWindow>();
                        UnityEngine.Object.DontDestroyOnLoad(atcWindowObj);
                        
                        GameObject mapOverlayObj = new GameObject("NO_ATC_MapOverlay");
                        mapOverlay = mapOverlayObj.AddComponent<UI.MapOverlay>();
                        UnityEngine.Object.DontDestroyOnLoad(mapOverlayObj);
                        
                        initialized = true;
                        Plugin.Log.LogInfo("[ATC] ATC Component initialized!");
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogError($"[ATC] Error initializing: {ex.Message}\n{ex.StackTrace}");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(TacScreen), "Update")]
        public static class OnPlatformUpdate
        {
            public static void Postfix()
            {
                if (!initialized || !Plugin.Enabled.Value) return;

                try
                {
                    float currentTime = Time.time;
                    
                    // Update radar system
                    if (currentTime - lastUpdateTime >= Plugin.RadarUpdateInterval.Value)
                    {
                        radarSystem?.Update();
                        lastUpdateTime = currentTime;
                    }

                    atcWindow?.Update();
                    mapOverlay?.UpdateOverlay();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ATC] Error in OnPlatformUpdate: {ex.Message}");
                }
            }
        }

        public static void Cleanup()
        {
            try
            {
                atcWindow?.Cleanup();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ATC] Error during cleanup: {ex.Message}");
            }
            radarSystem = null;
            atcWindow = null;
            if (mapOverlay != null && mapOverlay.gameObject != null)
            {
                UnityEngine.Object.Destroy(mapOverlay.gameObject);
            }
            mapOverlay = null;
            initialized = false;
        }

        [HarmonyPatch(typeof(DynamicMap), "UpdateMap")]
        public static class MapUpdatePatch
        {
            public static void Postfix()
            {
                if (!initialized || !Plugin.Enabled.Value) return;
                
                try
                {
                    if (Plugin.EnableMapOverlay.Value && mapOverlay != null)
                    {
                        mapOverlay.UpdateOverlay();
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[ATC] Error in MapUpdatePatch: {ex.Message}");
                }
            }
        }

        public static RadarSystem GetRadarSystem() => radarSystem;
        public static ATCWindow GetWindow() => atcWindow;
        
        public static Aircraft GetPlayerAircraft()
        {
            try
            {
                var combatHUD = SceneSingleton<CombatHUD>.i;
                if (combatHUD != null && combatHUD.aircraft != null)
                {
                    return combatHUD.aircraft;
                }
                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ATCComponent] Error getting player aircraft: {ex.Message}");
                return null;
            }
        }
    }
}
