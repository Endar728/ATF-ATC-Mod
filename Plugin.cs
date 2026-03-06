using System;
using System.IO;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using NO_ATC_Mod.Core;
using NO_ATC_Mod.UI;

namespace NO_ATC_Mod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static Harmony harmony;
        public static ManualLogSource Log;

        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> ShowATCWindow;
        public static ConfigEntry<KeyCode> ToggleWindowKey;
        public static ConfigEntry<float> RadarRange;
        public static ConfigEntry<float> RadarUpdateInterval;
        public static ConfigEntry<bool> ShowBRAA;
        public static ConfigEntry<bool> ShowUnitInfo;
        public static ConfigEntry<bool> ShowFriendly;
        public static ConfigEntry<bool> ShowHostile;
        public static ConfigEntry<Color> ATCColor;
        
        // Unit type filters (what to track and display)
        public static ConfigEntry<bool> ShowAircraft;
        public static ConfigEntry<bool> ShowShips;
        public static ConfigEntry<bool> ShowGroundVehicles;
        public static ConfigEntry<bool> ShowBuildings;
        
        public static ConfigEntry<bool> UseImperialUnits;
        
        public static ConfigEntry<bool> EnableMapOverlay;
        public static ConfigEntry<bool> MapOverlayShowDistance;
        public static ConfigEntry<bool> MapOverlayShowAltitude;
        public static ConfigEntry<bool> MapOverlayShowSpeed;
        public static ConfigEntry<bool> MapOverlayShowHeading;
        public static ConfigEntry<float> WindowPosX;
        public static ConfigEntry<float> WindowPosY;
        public static ConfigEntry<float> WindowWidth;
        public static ConfigEntry<float> WindowHeight;
        public static ConfigEntry<bool> WindowTransparent;
        
        // Radar Coverage (Optional)
        public static ConfigEntry<bool> EnableRadarCoverage;
        public static ConfigEntry<float> RadarMaxElevation;
        public static ConfigEntry<float> RadarMinElevation;
        public static ConfigEntry<bool> UseTerrainMasking;

        private void Awake()
        {
            // Register assembly resolver for MonoMod.Backports
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            
            Log = Logger;
            
            try
            {
                var backportsAssembly = System.Reflection.Assembly.Load("MonoMod.Backports, Version=1.1.2.0, Culture=neutral, PublicKeyToken=null");
                Log.LogInfo("[Plugin] MonoMod.Backports.dll loaded successfully");
            }
            catch (System.Exception ex)
            {
                Log.LogWarning($"[Plugin] Could not pre-load MonoMod.Backports: {ex.Message}. It will be loaded on-demand.");
            }
            
            harmony = new Harmony(PluginInfo.PLUGIN_GUID);

            // Configuration
            Enabled = Config.Bind("General", "Enabled", true, "Enable/disable the ATC mod");
            ShowATCWindow = Config.Bind("General", "Show ATC Window", true, "Show the ATC radar window");
            ToggleWindowKey = Config.Bind("General", "Toggle Window Key", KeyCode.F9, "Key to toggle ATC window");
            
            RadarRange = Config.Bind("Radar", "Radar Range", 50000f, "Maximum radar detection range in meters");
            RadarUpdateInterval = Config.Bind("Radar", "Update Interval", 0.25f, "Radar update interval in seconds (higher = less lag, lower = more responsive)");
            
            ShowBRAA = Config.Bind("Display", "Show BRAA", true, "Show Bearing, Range, Altitude, Aspect information");
            ShowUnitInfo = Config.Bind("Display", "Show Unit Info", true, "Show detailed unit information");
            ShowFriendly = Config.Bind("Display", "Show Friendly Units", true, "Show friendly units in radar display");
            ShowHostile = Config.Bind("Display", "Show Hostile Units", true, "Show hostile units in radar display");
            ATCColor = Config.Bind("Display", "ATC Color", new Color(0f, 1f, 0f), "Main ATC display color");
            
            ShowAircraft = Config.Bind("Display", "Show Aircraft", true, "Show aircraft in ATC list");
            ShowShips = Config.Bind("Display", "Show Ships", true, "Show ships in ATC list");
            ShowGroundVehicles = Config.Bind("Display", "Show Ground Vehicles", false, "Show ground vehicles in ATC list");
            ShowBuildings = Config.Bind("Display", "Show Buildings", false, "Show buildings in ATC list");
            
            UseImperialUnits = Config.Bind("Display", "Use Imperial Units", true, "Use Imperial units (nm, ft, kt). If false, uses Metric (km, m, km/h)");
            
            EnableMapOverlay = Config.Bind("Map Overlay", "Enable Map Overlay", false, "Display ATC unit information directly on the map as text labels");
            MapOverlayShowDistance = Config.Bind("Map Overlay", "Show Distance", true, "Show distance on map overlay");
            MapOverlayShowAltitude = Config.Bind("Map Overlay", "Show Altitude", true, "Show altitude on map overlay");
            MapOverlayShowSpeed = Config.Bind("Map Overlay", "Show Speed", true, "Show speed on map overlay");
            MapOverlayShowHeading = Config.Bind("Map Overlay", "Show Heading", true, "Show heading on map overlay");
            
            WindowPosX = Config.Bind("Window", "Position X", 10f, "Window X position");
            WindowPosY = Config.Bind("Window", "Position Y", 10f, "Window Y position");
            WindowWidth = Config.Bind("Window", "Width", 480f, "Window width");
            WindowHeight = Config.Bind("Window", "Height", 600f, "Window height");
            WindowTransparent = Config.Bind("Window", "Transparent background", false, "If true, window background is semi-transparent; if false, solid black.");
            
            EnableRadarCoverage = Config.Bind("Radar Coverage", "Enable Radar Coverage", false, 
                "Enable realistic radar coverage simulation with terrain masking and elevation limits. WARNING: May filter out many aircraft.");
            RadarMaxElevation = Config.Bind("Radar Coverage", "Max Elevation", 50000f, 
                "Maximum altitude for radar detection in meters");
            RadarMinElevation = Config.Bind("Radar Coverage", "Min Elevation", 0f, 
                "Minimum altitude for radar detection in meters");
            UseTerrainMasking = Config.Bind("Radar Coverage", "Use Terrain Masking", false, 
                "Enable terrain masking (units behind terrain cannot be detected). WARNING: Very aggressive, may only show aircraft directly on final approach.");

            string pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            Core.GameAssetsLoader.SetIconsFolder(pluginDir ?? "");

            if (Enabled.Value)
            {
                Log.LogInfo($"NO ATC Mod v{PluginInfo.PLUGIN_VERSION} loaded!");
                
                try
                {
                    harmony.PatchAll(typeof(ATCComponent.OnPlatformStart));
                    harmony.PatchAll(typeof(ATCComponent.OnPlatformUpdate));
                    Log.LogInfo("ATC Mod patches applied successfully!");
                }
                catch (System.Exception ex)
                {
                    Log.LogError($"[Plugin] Error applying patches: {ex.Message}\n{ex.StackTrace}");
                    throw;
                }
            }
            else
            {
                Log.LogInfo("ATC Mod is disabled in configuration.");
            }
        }

        private System.Reflection.Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            if (args.Name.StartsWith("MonoMod.Backports") || args.Name.StartsWith("MonoMod.ILHelpers"))
            {
                try
                {
                    string assemblyName = args.Name.Split(',')[0];
                    string dllName = assemblyName + ".dll";
                    
                    // Try to load from plugins folder (same directory as mod DLL)
                    string pluginPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(pluginPath))
                    {
                        string dllPath = System.IO.Path.Combine(pluginPath, dllName);
                        if (System.IO.File.Exists(dllPath))
                        {
                            return System.Reflection.Assembly.LoadFrom(dllPath);
                        }
                    }
                    
                    string bepInExPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "..", "..", "core", dllName);
                    bepInExPath = System.IO.Path.GetFullPath(bepInExPath);
                    if (System.IO.File.Exists(bepInExPath))
                    {
                        return System.Reflection.Assembly.LoadFrom(bepInExPath);
                    }
                }
                catch (System.Exception ex)
                {
                    Log?.LogError($"[Plugin] Error resolving {args.Name}: {ex.Message}");
                }
            }
            return null;
        }

        private void OnDestroy()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve;
            harmony?.UnpatchSelf();
            ATCComponent.Cleanup();
            Core.GameAssetsLoader.ClearCache();
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.noatcmod.ATC";
        public const string PLUGIN_NAME = "NO ATC Mod";
        public const string PLUGIN_VERSION = "1.0.0";
    }
}
