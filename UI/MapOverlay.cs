using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using NO_ATC_Mod.Core;

namespace NO_ATC_Mod.UI
{
    public class MapOverlay : MonoBehaviour
    {
        private Dictionary<Unit, GameObject> overlayLabels = new Dictionary<Unit, GameObject>();
        private Dictionary<Aircraft, string> callsignCache = new Dictionary<Aircraft, string>();
        private Dictionary<Unit, UnitMapIcon> unitIconCache = new Dictionary<Unit, UnitMapIcon>(); // Cache UnitMapIcon lookups
        private GameObject mapImageParent = null;
        private float lastUpdateTime = 0f;
        private float lastCallsignCacheClear = 0f;
        private float lastIconCacheRefresh = 0f;
        private const float UPDATE_INTERVAL = 0.2f; // Update overlay every 0.2 seconds (reduced frequency)
        private const float CALLSIGN_CACHE_CLEAR_INTERVAL = 5f; // Clear cache every 5 seconds
        private const float ICON_CACHE_REFRESH_INTERVAL = 1f; // Refresh icon cache every 1 second (much less frequent)
        
        // Cached map conversion parameters (derived from AirbaseMapIcon positions)
        private float cachedMapScale = 0f;
        private Vector2 cachedMapOffset = Vector2.zero;
        private bool hasValidConversion = false;
        private int conversionLockCount = 0; // Track how many times we've tried to calculate
        private const int MAX_CONVERSION_ATTEMPTS = 3; // Stop trying after 3 successful locks
        
        // Use LabelFormatter for consistent formatting
        private string FormatDistance(float meters) => LabelFormatter.FormatDistance(meters);
        private string FormatAltitude(float meters, float refAlt = 0f) => LabelFormatter.FormatAltitudeShort(meters, refAlt, null);
        private string FormatSpeed(float metersPerSecond) => LabelFormatter.FormatSpeed(metersPerSecond, null);
        
        // Get reference point (AWACS mode support)
        private (Vector3 position, Vector3 velocity, float altitude) GetReferencePoint()
        {
            var radarSystem = ATCComponent.GetRadarSystem();
            if (radarSystem != null)
            {
                var (pos, vel) = radarSystem.GetReferencePoint();
                return (pos, vel, pos.y);
            }
            
            var playerAircraft = ATCComponent.GetPlayerAircraft();
            if (playerAircraft != null && playerAircraft.rb != null)
            {
                var pos = playerAircraft.rb.transform.position;
                return (pos, playerAircraft.rb.velocity, pos.y);
            }
            
            return (Vector3.zero, Vector3.zero, 0f);
        }
        
        // Calculate distance from reference point
        private float GetDistanceFromReference(Vector3 position)
        {
            var (refPos, _, _) = GetReferencePoint();
            return Vector3.Distance(refPos, position);
        }
        
        public void UpdateOverlay()
        {
            if (!Plugin.EnableMapOverlay.Value || !Plugin.Enabled.Value)
            {
                ClearAllLabels();
                return;
            }
            
            // Throttle updates
            float currentTime = Time.time;
            if (currentTime - lastUpdateTime < UPDATE_INTERVAL)
            {
                return;
            }
            lastUpdateTime = currentTime;
            
            try
            {
                var map = SceneSingleton<DynamicMap>.i;
                if (map == null)
                {
                    ClearAllLabels();
                    return;
                }
                
                // Get mapImage via reflection
                var mapType = map.GetType();
                var mapImageField = mapType.GetField("mapImage");
                if (mapImageField == null)
                {
                    return;
                }
                
                var mapImage = mapImageField.GetValue(map) as GameObject;
                if (mapImage == null)
                {
                    ClearAllLabels();
                    return;
                }
                
                // Cache mapImage parent
                if (mapImageParent != mapImage)
                {
                    mapImageParent = mapImage;
                    ClearAllLabels(); // Clear old labels when map changes
                    unitIconCache.Clear(); // Clear icon cache when map changes
                    ResetMapConversion(); // Recalculate conversion for new map
                }
                
                var radarSystem = ATCComponent.GetRadarSystem();
                if (radarSystem == null)
                {
                    return;
                }
                
                var trackedUnits = radarSystem.GetTrackedUnits();
                
                // Clear callsign cache periodically
                if (currentTime - lastCallsignCacheClear >= CALLSIGN_CACHE_CLEAR_INTERVAL)
                {
                    callsignCache.Clear();
                    lastCallsignCacheClear = currentTime;
                }
                
                // Refresh UnitMapIcon cache periodically (much less frequent than updates)
                if (currentTime - lastIconCacheRefresh >= ICON_CACHE_REFRESH_INTERVAL)
                {
                    RefreshIconCache();
                    lastIconCacheRefresh = currentTime;
                }
                
                // Get map dimension for coordinate conversion
                var mapDimensionField = mapType.GetField("mapDimension");
                float mapDimension = 900f; // Default
                if (mapDimensionField != null)
                {
                    var dimValue = mapDimensionField.GetValue(map);
                    if (dimValue != null)
                    {
                        mapDimension = (float)dimValue;
                    }
                }
                
                float factor = 900f / mapDimension;
                float zoom = mapImage.transform.localScale.x;
                
                // Filter units based on friendly/hostile settings
                var filteredUnits = trackedUnits.Where(u => 
                    (u.isFriendly && Plugin.ShowFriendly.Value) || 
                    (!u.isFriendly && Plugin.ShowHostile.Value)
                ).ToList();
                
                // Create/update labels for tracked units
                var unitsToKeep = new HashSet<Unit>();
                foreach (var tracked in filteredUnits)
                {
                    if (tracked.unit == null) continue;
                    
                    unitsToKeep.Add(tracked.unit);
                    
                    // Get unit position on map - use the same coordinate conversion as the game
                    Vector3 unitGlobalPos = tracked.position;
                    Vector3 unitMapPos = new Vector3(unitGlobalPos.x * factor, unitGlobalPos.z * factor, 0f);
                    
                    // Get UnitMapIcon from cache (much faster than FindObjectsOfType)
                    UnitMapIcon unitIcon = null;
                    unitIconCache.TryGetValue(tracked.unit, out unitIcon);
                    
                    // Create or update label
                    if (!overlayLabels.ContainsKey(tracked.unit))
                    {
                        CreateLabel(tracked.unit, mapImage.transform, unitIcon);
                    }
                    
                    // Update label position and text
                    if (overlayLabels.TryGetValue(tracked.unit, out GameObject labelObj))
                    {
                        UpdateLabel(labelObj, tracked, unitMapPos, zoom, unitIcon);
                    }
                }
                
                // Remove labels for units no longer tracked
                var unitsToRemove = new List<Unit>();
                foreach (var kvp in overlayLabels)
                {
                    if (!unitsToKeep.Contains(kvp.Key))
                    {
                        unitsToRemove.Add(kvp.Key);
                    }
                }
                
                foreach (var unit in unitsToRemove)
                {
                    RemoveLabel(unit);
                    unitIconCache.Remove(unit); // Also remove from icon cache
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MapOverlay] Error in UpdateOverlay: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private void RefreshIconCache()
        {
            try
            {
                // Clear old cache
                unitIconCache.Clear();
                
                // Find all UnitMapIcons (only once per refresh interval, not every update)
                var allIcons = UnityEngine.Object.FindObjectsOfType<UnitMapIcon>();
                foreach (var icon in allIcons)
                {
                    if (icon.unit != null)
                    {
                        unitIconCache[icon.unit] = icon;
                    }
                }
                
                // Derive map conversion from STATIC airbase icons (only if not already calculated)
                if (!hasValidConversion)
                {
                    UpdateMapConversionFromAirbases();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MapOverlay] Error refreshing icon cache: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Calculate the proper world-to-map coordinate conversion using existing map icons.
        /// We try AirbaseMapIcon first (static), then UnitMapIcon (dynamic but only sampled once).
        /// </summary>
        private void UpdateMapConversionFromAirbases()
        {
            // Don't recalculate if already locked successfully
            if (hasValidConversion && conversionLockCount >= MAX_CONVERSION_ATTEMPTS)
            {
                return;
            }
            
            try
            {
                // First, try to use the DynamicMap's own conversion method
                var map = SceneSingleton<DynamicMap>.i;
                if (map != null)
                {
                    var mapType = map.GetType();
                    var mapDimensionField = mapType.GetField("mapDimension");
                    if (mapDimensionField != null)
                    {
                        var dimValue = mapDimensionField.GetValue(map);
                        if (dimValue != null)
                        {
                            float mapDimension = (float)dimValue;
                            float factor = 900f / mapDimension;
                            
                            Plugin.Log?.LogInfo($"[MapOverlay] Map factor calculated: {factor:F6} (mapDimension={mapDimension})");
                            
                            // Try 1: Use AirbaseMapIcon (static, preferred)
                            var airbaseIcons = UnityEngine.Object.FindObjectsOfType<AirbaseMapIcon>();
                            Plugin.Log?.LogInfo($"[MapOverlay] Found {airbaseIcons?.Length ?? 0} AirbaseMapIcon objects");
                            
                            if (airbaseIcons != null && airbaseIcons.Length > 0)
                            {
                                foreach (var airbaseIcon in airbaseIcons)
                                {
                                    if (airbaseIcon == null || airbaseIcon.transform == null) continue;
                                    
                                    // Log all fields on the airbase icon for debugging
                                    var iconType = airbaseIcon.GetType();
                                    var allFields = iconType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                    Plugin.Log?.LogInfo($"[MapOverlay] AirbaseMapIcon fields: {string.Join(", ", allFields.Select(f => f.Name))}");
                                    
                                    // Try to get the airbase's world position - check all possible field names
                                    Vector3 worldPos = Vector3.zero;
                                    bool foundWorldPos = false;
                                    
                                    // Try various field names that might contain the world position reference
                                    string[] possibleFieldNames = { "airbase", "unit", "targetUnit", "target", "linkedUnit", "baseUnit" };
                                    
                                    foreach (var fieldName in possibleFieldNames)
                                    {
                                        var field = iconType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (field != null)
                                        {
                                            var value = field.GetValue(airbaseIcon);
                                            if (value != null)
                                            {
                                                // Try to get transform from it
                                                var component = value as Component;
                                                if (component != null && component.transform != null)
                                                {
                                                    worldPos = component.transform.position;
                                                    foundWorldPos = true;
                                                    Plugin.Log?.LogInfo($"[MapOverlay] Got world pos from AirbaseMapIcon.{fieldName}: {worldPos}");
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                    
                                    if (foundWorldPos && worldPos.sqrMagnitude > 100f)
                                    {
                                        Vector3 mapPos = airbaseIcon.transform.localPosition;
                                        Vector3 factorPos = new Vector3(worldPos.x * factor, worldPos.z * factor, 0f);
                                        
                                        cachedMapOffset = new Vector2(
                                            mapPos.x - factorPos.x,
                                            mapPos.y - factorPos.y
                                        );
                                        cachedMapScale = factor;
                                        hasValidConversion = true;
                                        conversionLockCount++;
                                        
                                        Plugin.Log?.LogInfo($"[MapOverlay] Map conversion LOCKED via AirbaseMapIcon (attempt {conversionLockCount}): " +
                                            $"factor={factor:F6}, offset=({cachedMapOffset.x:F1}, {cachedMapOffset.y:F1}), " +
                                            $"worldPos={worldPos}, mapPos={mapPos}");
                                        return;
                                    }
                                }
                            }
                            
                            // Try 2: Use UnitMapIcon (dynamic, but we only sample once)
                            // This works because even though units move, the conversion formula is constant
                            if (unitIconCache.Count > 0)
                            {
                                Plugin.Log?.LogInfo($"[MapOverlay] Trying UnitMapIcon conversion (have {unitIconCache.Count} cached icons)");
                                
                                foreach (var kvp in unitIconCache)
                                {
                                    var unit = kvp.Key;
                                    var unitIcon = kvp.Value;
                                    
                                    if (unit == null || unitIcon == null || unitIcon.transform == null)
                                        continue;
                                    
                                    Vector3 worldPos = unit.transform.position;
                                    Vector3 mapPos = unitIcon.transform.localPosition;
                                    
                                    // Skip units at origin or with zero map position
                                    if (worldPos.sqrMagnitude < 100f || mapPos.sqrMagnitude < 1f)
                                        continue;
                                    
                                    Vector3 factorPos = new Vector3(worldPos.x * factor, worldPos.z * factor, 0f);
                                    
                                    cachedMapOffset = new Vector2(
                                        mapPos.x - factorPos.x,
                                        mapPos.y - factorPos.y
                                    );
                                    cachedMapScale = factor;
                                    hasValidConversion = true;
                                    conversionLockCount++;
                                    
                                    Plugin.Log?.LogInfo($"[MapOverlay] Map conversion LOCKED via UnitMapIcon (attempt {conversionLockCount}): " +
                                        $"factor={factor:F6}, offset=({cachedMapOffset.x:F1}, {cachedMapOffset.y:F1}), " +
                                        $"worldPos={worldPos}, mapPos={mapPos}, unit={unit.name}");
                                    return;
                                }
                            }
                            
                            // Try 3: Use ALL UnitMapIcons fresh (not from cache)
                            var allUnitIcons = UnityEngine.Object.FindObjectsOfType<UnitMapIcon>();
                            Plugin.Log?.LogInfo($"[MapOverlay] Found {allUnitIcons?.Length ?? 0} total UnitMapIcon objects");
                            
                            if (allUnitIcons != null && allUnitIcons.Length > 0)
                            {
                                foreach (var unitIcon in allUnitIcons)
                                {
                                    if (unitIcon == null || unitIcon.transform == null || unitIcon.unit == null)
                                        continue;
                                    
                                    Vector3 worldPos = unitIcon.unit.transform.position;
                                    Vector3 mapPos = unitIcon.transform.localPosition;
                                    
                                    // Skip units at origin or with zero map position
                                    if (worldPos.sqrMagnitude < 100f || mapPos.sqrMagnitude < 1f)
                                        continue;
                                    
                                    Vector3 factorPos = new Vector3(worldPos.x * factor, worldPos.z * factor, 0f);
                                    
                                    cachedMapOffset = new Vector2(
                                        mapPos.x - factorPos.x,
                                        mapPos.y - factorPos.y
                                    );
                                    cachedMapScale = factor;
                                    hasValidConversion = true;
                                    conversionLockCount++;
                                    
                                    Plugin.Log?.LogInfo($"[MapOverlay] Map conversion LOCKED via fresh UnitMapIcon (attempt {conversionLockCount}): " +
                                        $"factor={factor:F6}, offset=({cachedMapOffset.x:F1}, {cachedMapOffset.y:F1}), " +
                                        $"worldPos={worldPos}, mapPos={mapPos}, unit={unitIcon.unit.name}");
                                    return;
                                }
                            }
                            
                            // Fallback: Use just factor with zero offset (will likely be wrong, but at least shows something)
                            if (!hasValidConversion)
                            {
                                cachedMapScale = factor;
                                cachedMapOffset = Vector2.zero;
                                hasValidConversion = true;
                                conversionLockCount++;
                                
                                Plugin.Log?.LogWarning($"[MapOverlay] Map conversion LOCKED (FALLBACK - no icons found, attempt {conversionLockCount}): " +
                                    $"factor={factor:F6}, offset=(0, 0). Map positions may be incorrect!");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[MapOverlay] Error calculating map conversion: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Convert a world position to map coordinates using the derived conversion
        /// </summary>
        private Vector3 WorldToMapPosition(Vector3 worldPos)
        {
            if (hasValidConversion)
            {
                // Use derived conversion: mapPos = worldPos * scale + offset
                return new Vector3(
                    worldPos.x * cachedMapScale + cachedMapOffset.x,
                    worldPos.z * cachedMapScale + cachedMapOffset.y,
                    0f
                );
            }
            else
            {
                // Fallback to simple factor-based conversion
                var map = SceneSingleton<DynamicMap>.i;
                if (map != null)
                {
                    var mapType = map.GetType();
                    var mapDimensionField = mapType.GetField("mapDimension");
                    float mapDimension = 900f;
                    if (mapDimensionField != null)
                    {
                        var dimValue = mapDimensionField.GetValue(map);
                        if (dimValue != null)
                            mapDimension = (float)dimValue;
                    }
                    float factor = 900f / mapDimension;
                    return new Vector3(worldPos.x * factor, worldPos.z * factor, 0f);
                }
                return new Vector3(worldPos.x, worldPos.z, 0f);
            }
        }
        
        private void CreateLabel(Unit unit, Transform parent, UnitMapIcon unitIcon = null)
        {
            try
            {
                // Try to use UnitMapIcon's parent if available
                Transform actualParent = parent;
                if (unitIcon != null && unitIcon.transform != null && unitIcon.transform.parent != null)
                {
                    actualParent = unitIcon.transform.parent;
                }
                
                GameObject labelObj = new GameObject($"ATC_Overlay_{unit.GetInstanceID()}");
                labelObj.transform.SetParent(actualParent, false);
                
                // Add RectTransform - smaller size for compact display
                RectTransform rect = labelObj.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0, 0);
                rect.anchorMax = new Vector2(0, 0);
                rect.pivot = new Vector2(0, 1); // Top-left pivot for easier positioning
                rect.sizeDelta = new Vector2(80, 40); // Smaller size
                
                // Add background image with slight transparency
                Image bgImage = labelObj.AddComponent<Image>();
                bgImage.color = new Color(0, 0, 0, 0.6f);
                
                GameObject iconObj = new GameObject("Icon");
                iconObj.transform.SetParent(labelObj.transform, false);
                RectTransform iconRect = iconObj.AddComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.02f, 0.5f);
                iconRect.anchorMax = new Vector2(0.02f, 0.5f);
                iconRect.pivot = new Vector2(0, 0.5f);
                iconRect.sizeDelta = new Vector2(14, 14);
                iconRect.anchoredPosition = Vector2.zero;
                Image iconImage = iconObj.AddComponent<Image>();
                iconImage.color = Color.white;
                
                GameObject textObj = new GameObject("Text");
                textObj.transform.SetParent(labelObj.transform, false);
                RectTransform textRect = textObj.AddComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.22f, 0.05f);
                textRect.anchorMax = new Vector2(0.95f, 0.95f);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                
                Text text = textObj.AddComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                text.fontSize = 8; // Smaller font size
                text.color = Plugin.ATCColor.Value;
                text.alignment = TextAnchor.UpperLeft;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                
                overlayLabels[unit] = labelObj;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MapOverlay] Error creating label: {ex.Message}");
            }
        }
        
        private void UpdateLabel(GameObject labelObj, TrackedUnit tracked, Vector3 mapPos, float zoom, UnitMapIcon unitIcon = null)
        {
            try
            {
                RectTransform rect = labelObj.GetComponent<RectTransform>();
                if (rect == null) return;
                
                Vector3 finalPosition = mapPos;
                
                // If we have a UnitMapIcon, try to use its position directly
                if (unitIcon != null && unitIcon.transform != null)
                {
                    finalPosition = unitIcon.transform.localPosition;
                }
                else
                {
                    // Position label near unit icon (smaller offset, positioned to the right of icon)
                    // Scale offset with zoom to keep it consistent
                    float offsetX = 8f / zoom;
                    float offsetY = 8f / zoom;
                    finalPosition = mapPos + new Vector3(offsetX, offsetY, 0f);
                }
                
                rect.localPosition = finalPosition;
                rect.localScale = Vector3.one / zoom;

                Transform iconTransform = labelObj.transform.Find("Icon");
                if (iconTransform != null)
                {
                    Image iconImage = iconTransform.GetComponent<Image>();
                    Texture2D iconTex = GameAssetsLoader.GetUnitIconTexture(tracked.unit, tracked.isFriendly);
                    if (iconImage != null && iconTex != null)
                    {
                        iconImage.sprite = GameAssetsLoader.GetOrCreateSprite(iconTex);
                        iconImage.enabled = true;
                    }
                }

                Text text = labelObj.transform.Find("Text")?.GetComponent<Text>();
                if (text != null)
                {
                    StringBuilder sb = new StringBuilder();
                    
                    // First line: Unit name with TN# (if enabled) and vertical indicator
                    string displayName = GetUnitDisplayName(tracked);
                    string vertIndicator = LabelFormatter.GetVerticalIndicator(tracked.verticalTrend, "nostable");
                    
                    // Show TN# for all units if enabled
                    if (Plugin.ShowTrackNumbers.Value)
                    {
                        // Show TN# plus callsign for friendlies, just TN# for hostiles
                        if (tracked.isFriendly)
                        {
                            sb.Append($"TN#{tracked.trackNumber} {displayName}");
                        }
                        else
                        {
                            sb.Append($"TN#{tracked.trackNumber}");
                        }
                        if (!string.IsNullOrEmpty(vertIndicator))
                            sb.Append($" {vertIndicator}");
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.Append(displayName);
                        if (!string.IsNullOrEmpty(vertIndicator))
                            sb.Append($" {vertIndicator}");
                        sb.AppendLine();
                    }
                    
                    // Get reference point altitude (AWACS mode support)
                    var (refPos, refVel, refAlt) = GetReferencePoint();
                    
                    // Calculate distance from reference point (not from player if in AWACS mode)
                    float distFromRef = Vector3.Distance(refPos, tracked.position);
                    
                    // Additional info lines (compact format)
                    if (Plugin.MapOverlayShowDistance.Value)
                    {
                        sb.Append($"{FormatDistance(distFromRef)} ");
                    }
                    if (Plugin.MapOverlayShowAltitude.Value)
                    {
                        sb.Append($"{FormatAltitude(tracked.altitude, refAlt)} ");
                    }
                    if (Plugin.MapOverlayShowSpeed.Value)
                    {
                        sb.Append($"{FormatSpeed(tracked.speed)} ");
                    }
                    if (Plugin.MapOverlayShowHeading.Value)
                    {
                        sb.Append($"{tracked.heading:F0}°");
                    }
                    
                    text.text = sb.ToString().Trim();
                    text.color = tracked.isFriendly ? Color.green : Color.red;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[MapOverlay] Error updating label: {ex.Message}");
            }
        }
        
        private string GetUnitDisplayName(TrackedUnit tracked)
        {
            if (tracked.unit == null) return "Unknown";
            
            Aircraft? aircraft = tracked.unit as Aircraft;
            
            if (aircraft != null)
            {
                // Check if it's the local player's aircraft
                var playerAircraft = ATCComponent.GetPlayerAircraft();
                if (aircraft == playerAircraft)
                {
                    return "PLAYER";
                }
                
                // Try to get pilot name from cache
                if (callsignCache.TryGetValue(aircraft, out string cachedCallsign))
                {
                    return cachedCallsign;
                }
                
                // Try to get pilot name via reflection (try field first, then property)
                try
                {
                    if (aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null)
                    {
                        var pilot = aircraft.pilots[0];
                        
                        // Try field access first (more common in Unity)
                        var playerField = pilot.GetType().GetField("player", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (playerField != null)
                        {
                            var player = playerField.GetValue(pilot);
                            if (player != null)
                            {
                                // Try field for PlayerName
                                var nameField = player.GetType().GetField("PlayerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (nameField != null)
                                {
                                    string playerName = nameField.GetValue(player) as string;
                                    if (!string.IsNullOrEmpty(playerName))
                                    {
                                        callsignCache[aircraft] = playerName;
                                        return playerName;
                                    }
                                }
                                
                                // Try property for PlayerName
                                var nameProp = player.GetType().GetProperty("PlayerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (nameProp != null)
                                {
                                    string playerName = nameProp.GetValue(player) as string;
                                    if (!string.IsNullOrEmpty(playerName))
                                    {
                                        callsignCache[aircraft] = playerName;
                                        return playerName;
                                    }
                                }
                            }
                        }
                        
                        // Fallback: try property for player
                        var playerProp = pilot.GetType().GetProperty("player", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (playerProp != null)
                        {
                            var player = playerProp.GetValue(pilot);
                            if (player != null)
                            {
                                var nameProp = player.GetType().GetProperty("PlayerName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (nameProp != null)
                                {
                                    string playerName = nameProp.GetValue(player) as string;
                                    if (!string.IsNullOrEmpty(playerName))
                                    {
                                        callsignCache[aircraft] = playerName;
                                        return playerName;
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error occasionally for debugging
                    if (UnityEngine.Random.value < 0.01f) // Only log 1% of errors to avoid spam
                    {
                        Plugin.Log.LogWarning($"[MapOverlay] Error getting player name: {ex.Message}");
                    }
                }
            }
            
            // Fallback to GameObject name
            return tracked.unit.gameObject.name;
        }
        
        private void RemoveLabel(Unit unit)
        {
            if (overlayLabels.TryGetValue(unit, out GameObject labelObj))
            {
                if (labelObj != null)
                {
                    UnityEngine.Object.Destroy(labelObj);
                }
                overlayLabels.Remove(unit);
            }
        }
        
        private void ClearAllLabels()
        {
            foreach (var kvp in overlayLabels)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.Destroy(kvp.Value);
                }
            }
            overlayLabels.Clear();
            unitIconCache.Clear(); // Also clear icon cache
            // Note: DON'T reset hasValidConversion here - the conversion should stay stable
            // It only needs to be recalculated when the map itself changes
        }
        
        /// <summary>
        /// Force recalculation of map conversion (call when map changes)
        /// </summary>
        public void ResetMapConversion()
        {
            hasValidConversion = false;
            cachedMapScale = 0f;
            cachedMapOffset = Vector2.zero;
            conversionLockCount = 0; // Reset lock count to allow recalculation
            Plugin.Log?.LogInfo("[MapOverlay] Map conversion reset, will recalculate");
        }
        
        void OnDestroy()
        {
            ClearAllLabels();
        }
    }
}
