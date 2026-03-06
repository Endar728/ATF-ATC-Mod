using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NO_ATC_Mod.Core
{
    public class TrackedUnit
    {
        public Unit unit;
        public Vector3 position;
        public Vector3 velocity;
        public float altitude;
        public float speed;
        public float heading;
        public float distance;
        public bool isFriendly;
        public string? callsign;
        public float lastUpdateTime;
        public bool isTracked;

        public TrackedUnit(Unit unit, Vector3 playerPosition, FactionHQ playerHQ)
        {
            this.unit = unit;
            Update(playerPosition, playerHQ);
        }

        public void Update(Vector3 playerPosition, FactionHQ playerHQ)
        {
            if (unit == null || unit.rb == null)
            {
                return;
            }

            try
            {
                position = unit.rb.transform.position;
                velocity = unit.rb.velocity;
                altitude = position.y;
                speed = velocity.magnitude;
                distance = Vector3.Distance(playerPosition, position);
                
                if (unit.NetworkHQ != null && playerHQ != null)
                {
                    isFriendly = unit.NetworkHQ == playerHQ;
                }
                else
                {
                    isFriendly = false;
                }
                
                if (velocity.sqrMagnitude > 1f)
                {
                    Vector3 flatVel = Vector3.ProjectOnPlane(velocity, Vector3.up);
                    heading = Quaternion.LookRotation(flatVel).eulerAngles.y;
                }
                
                lastUpdateTime = Time.time;
                isTracked = true;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[TrackedUnit] Error updating: {ex.Message}");
            }
        }

        public BRAAInfo CalculateBRAA(Vector3 playerPosition, Vector3 playerVelocity)
        {
            Vector3 relativePos = position - playerPosition;
            float range = relativePos.magnitude;
            float bearing = Mathf.Atan2(relativePos.x, relativePos.z) * Mathf.Rad2Deg;
            if (bearing < 0) bearing += 360f;
            
            float altitudeDiff = altitude - playerPosition.y;
            
            Vector3 playerForward = Vector3.ProjectOnPlane(playerVelocity, Vector3.up).normalized;
            Vector3 toTarget = Vector3.ProjectOnPlane(relativePos, Vector3.up).normalized;
            float aspect = Vector3.Angle(playerForward, toTarget);
            
            return new BRAAInfo
            {
                Bearing = bearing,
                Range = range,
                Altitude = altitude,
                AltitudeDiff = altitudeDiff,
                Aspect = aspect
            };
        }
    }

    public struct BRAAInfo
    {
        public float Bearing;
        public float Range;
        public float Altitude;
        public float AltitudeDiff;
        public float Aspect;
    }

    public class RadarSystem
    {
        private Dictionary<Unit, TrackedUnit> trackedUnits = new Dictionary<Unit, TrackedUnit>();
        private float maxRange;
        private RadarCoverage radarCoverage;
        
        // Performance optimization: Cache unit list and refresh periodically
        private List<Unit> cachedUnits = new List<Unit>();
        private float lastUnitCacheTime = 0f;
        private const float UNIT_CACHE_REFRESH_INTERVAL = 5f;

        public RadarSystem()
        {
            maxRange = Plugin.RadarRange.Value;
            radarCoverage = new RadarCoverage();
        }

        public void Update()
        {
            try
            {
                var playerAircraft = GetPlayerAircraft();
                if (playerAircraft == null) return;

                Vector3 playerPosition = playerAircraft.rb != null ? playerAircraft.rb.transform.position : Vector3.zero;
                FactionHQ playerHQ = playerAircraft.NetworkHQ;
                maxRange = Plugin.RadarRange.Value;

                float currentTime = Time.time;
                if (currentTime - lastUnitCacheTime >= UNIT_CACHE_REFRESH_INTERVAL)
                {
                    // Use FindObjectsOfType<Unit> directly instead of filtering all objects
                    cachedUnits = UnityEngine.Object.FindObjectsOfType<Unit>().ToList();
                    lastUnitCacheTime = currentTime;
                }

                var unitsInRange = new HashSet<Unit>();
                float maxRangeSqr = maxRange * maxRange; // Use squared distance for faster comparison

                foreach (var unit in cachedUnits)
                {
                    if (unit == null || unit.rb == null || unit == playerAircraft) continue;
                    
                    if (unit is Missile) continue;

                    bool isAircraft = unit is Aircraft;
                    bool isGround = unit is GroundVehicle;
                    bool isBuilding = unit is Building;
                    bool isShip = !isAircraft && !isGround && !isBuilding;
                    bool show = (isAircraft && Plugin.ShowAircraft.Value) ||
                                (isShip && Plugin.ShowShips.Value) ||
                                (isGround && Plugin.ShowGroundVehicles.Value) ||
                                (isBuilding && Plugin.ShowBuildings.Value);
                    if (!show) continue;
                    
                    // Additional safety: Check unit name for common munition patterns
                    // This is a fallback in case there are other projectile types
                    if (unit.gameObject != null)
                    {
                        string unitName = unit.gameObject.name.ToLower();
                        // Skip units with names suggesting they're projectiles/munitions
                        if (unitName.Contains("missile") || 
                            unitName.Contains("projectile") || 
                            unitName.Contains("bullet") ||
                            unitName.Contains("shell") ||
                            unitName.Contains("round"))
                        {
                            continue;
                        }
                    }

                    Vector3 unitPos = unit.rb.transform.position;
                    Vector3 offset = unitPos - playerPosition;
                    float distanceSqr = offset.sqrMagnitude;
                    
                    // Check if unit is within range (squared distance comparison)
                    if (distanceSqr <= maxRangeSqr)
                    {
                        // Optional: Check radar coverage (terrain masking, elevation limits)
                        bool canDetect = true;
                        if (Plugin.EnableRadarCoverage.Value)
                        {
                            if (Plugin.UseTerrainMasking.Value)
                            {
                                canDetect = radarCoverage.CanDetectUnit(
                                    playerPosition,
                                    unitPos,
                                    unitPos.y,
                                    Plugin.RadarMaxElevation.Value,
                                    Plugin.RadarMinElevation.Value
                                );
                            }
                            else
                            {
                                float unitAlt = unitPos.y;
                                canDetect = unitAlt <= Plugin.RadarMaxElevation.Value && 
                                           unitAlt >= Plugin.RadarMinElevation.Value;
                            }
                        }
                        
                        if (canDetect)
                        {
                            unitsInRange.Add(unit);
                            
                            if (!trackedUnits.ContainsKey(unit))
                            {
                                trackedUnits[unit] = new TrackedUnit(unit, playerPosition, playerHQ);
                            }
                            else
                            {
                                trackedUnits[unit].Update(playerPosition, playerHQ);
                            }
                        }
                    }
                }
                // Remove units that are out of range or destroyed
                var unitsToRemove = new List<Unit>();
                foreach (var kvp in trackedUnits)
                {
                    if (!unitsInRange.Contains(kvp.Key) || kvp.Key == null)
                    {
                        unitsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var unit in unitsToRemove)
                {
                    trackedUnits.Remove(unit);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[RadarSystem] Error in Update: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public List<TrackedUnit> GetTrackedUnits()
        {
            return trackedUnits.Values.ToList();
        }

        public TrackedUnit GetTrackedUnit(Unit unit)
        {
            trackedUnits.TryGetValue(unit, out TrackedUnit tracked);
            return tracked;
        }
        
        public static BRAAInfo CalculateBRAABetween(Vector3 fromPosition, Vector3 fromVelocity, Vector3 toPosition, float toAltitude)
        {
            Vector3 relativePos = toPosition - fromPosition;
            float range = relativePos.magnitude;
            float bearing = Mathf.Atan2(relativePos.x, relativePos.z) * Mathf.Rad2Deg;
            if (bearing < 0) bearing += 360f;
            
            float altitudeDiff = toAltitude - fromPosition.y;
            
            // Calculate aspect angle
            Vector3 fromForward = Vector3.ProjectOnPlane(fromVelocity, Vector3.up).normalized;
            Vector3 toTarget = Vector3.ProjectOnPlane(relativePos, Vector3.up).normalized;
            float aspect = Vector3.Angle(fromForward, toTarget);
            
            return new BRAAInfo
            {
                Bearing = bearing,
                Range = range,
                Altitude = toAltitude,
                AltitudeDiff = altitudeDiff,
                Aspect = aspect
            };
        }

        private Aircraft GetPlayerAircraft()
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
                Plugin.Log.LogError($"[RadarSystem] Error getting player aircraft: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Radar coverage simulation with terrain masking and elevation limits
    /// Similar to LotAtc's advanced radar coverage system
    /// </summary>
    public class RadarCoverage
    {
        private LayerMask terrainLayerMask = -1; // All layers by default
        
        public bool CanDetectUnit(Vector3 radarPosition, Vector3 unitPosition, float unitAltitude, 
            float maxElevation, float minElevation)
        {
            if (unitAltitude > maxElevation || unitAltitude < minElevation)
            {
                return false;
            }

            Vector3 direction = unitPosition - radarPosition;
            float distance = direction.magnitude;
            
            if (distance < 0.1f) return true;
            
            direction.Normalize();
            
            // Perform raycast to check for terrain blocking
            // Use a layer mask to only check for terrain/ground objects
            // Start raycast slightly above ground to avoid false positives
            Vector3 rayStart = radarPosition + Vector3.up * 10f; // Start 10m above player position
            RaycastHit[] hits = Physics.RaycastAll(rayStart, direction, distance, terrainLayerMask);
            
            bool blocked = false;
            foreach (var hit in hits)
            {
                if (hit.collider != null && hit.collider.gameObject != null)
                {
                    string objName = hit.collider.gameObject.name.ToLower();
                    bool isTerrain = objName.Contains("terrain") || objName.Contains("ground") ||
                                    objName.Contains("mountain") || objName.Contains("hill");
                    float distanceToUnit = Vector3.Distance(hit.point, unitPosition);
                    if (isTerrain && distanceToUnit > 50f)
                    {
                        blocked = true;
                        break;
                    }
                }
            }
            
            return !blocked;
        }
    }
}
